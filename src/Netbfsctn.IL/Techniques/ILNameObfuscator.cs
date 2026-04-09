using dnlib.DotNet;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILNameObfuscator : IObfuscationTechnique<ModuleDef>
{
    public string Name => "名前難読化 (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var assemblyName = module.Assembly?.Name?.String ?? "";
        var renamePublic = context.Options.EnableRenamePublic
            && !context.ExcludeRenamePublicModules.Contains(assemblyName);

        // 同一モジュール内 MemberRef (Class=TypeDef) 修正用マップ
        var sameModuleRenames = new Dictionary<(TypeDef type, string oldName), string>();

        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;

            ProcessType(type, context, result, renamePublic, sameModuleRenames);
        }

        // 同一モジュール内 MemberRef の修正
        if (sameModuleRenames.Count > 0)
            FixSameModuleMemberRefs(module, sameModuleRenames, context);

        // renamePublic 対象モジュールでは virtual/abstract もグループ単位でリネーム
        if (renamePublic)
        {
            RenameVirtualMethodGroups(module, context, result, sameModuleRenames);
            RenameVirtualPropertyGroups(module, context, result, sameModuleRenames);
            RenameVirtualEventGroups(module, context, result, sameModuleRenames);
        }

        // パラメーター名の難読化（全メソッド対象、型のアクセス修飾子に関係なく安全）
        RenameParameters(module, context, result);
    }

    /// <summary>
    /// virtual/abstract メソッドを基底定義 + 全 override でグループ化し、
    /// グループ単位で同一の難読化名にリネームする。
    /// </summary>
    private static void RenameVirtualMethodGroups(
        ModuleDef module, ObfuscationContext context, ObfuscationResult result,
        Dictionary<(TypeDef, string), string> sameModuleRenames)
    {
        // 全型の継承関係を構築: TypeDef → 基底 TypeDef (モジュール内のみ)
        var allTypes = module.GetTypes().ToList();
        var typeByFullName = new Dictionary<string, TypeDef>();
        foreach (var t in allTypes)
            typeByFullName[t.FullName] = t;

        // virtual メソッドをグループ化
        // Key: (ルート TypeDef, メソッド名, シグネチャ文字列) → グループ内の全 MethodDef
        var groups = new Dictionary<(TypeDef rootType, string name, string sig), List<MethodDef>>();
        var processed = new HashSet<MethodDef>();

        foreach (var type in allTypes)
        {
            foreach (var method in type.Methods)
            {
                if (!method.IsVirtual) continue;
                if (method.IsConstructor) continue;
                if (processed.Contains(method)) continue;
                if (method.ImplMap != null || method.IsPinvokeImpl) continue;

                // このメソッドの「ルート」を探す（最上位の基底定義）
                var rootMethod = FindVirtualRoot(method, type, typeByFullName);
                var rootType = rootMethod.DeclaringType;
                var sigStr = rootMethod.MethodSig?.ToString() ?? "";
                var key = (rootType, rootMethod.Name.String, sigStr);

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<MethodDef>();
                    groups[key] = group;
                }

                // ルートから下に辿って全 override を収集
                if (!processed.Contains(rootMethod))
                {
                    group.Add(rootMethod);
                    processed.Add(rootMethod);
                }
                if (!processed.Contains(method))
                {
                    group.Add(method);
                    processed.Add(method);
                }
            }
        }

        // 各 override を収集漏れなく追加
        foreach (var type in allTypes)
        {
            foreach (var method in type.Methods)
            {
                if (!method.IsVirtual || method.IsConstructor) continue;
                if (processed.Contains(method)) continue;
                if (method.ImplMap != null || method.IsPinvokeImpl) continue;

                var rootMethod = FindVirtualRoot(method, type, typeByFullName);
                var rootType = rootMethod.DeclaringType;
                var sigStr = rootMethod.MethodSig?.ToString() ?? "";
                var key = (rootType, rootMethod.Name.String, sigStr);

                if (groups.TryGetValue(key, out var group))
                {
                    group.Add(method);
                    processed.Add(method);
                }
            }
        }

        // グループ単位でリネーム
        var renamedGroups = 0;
        foreach (var (key, group) in groups)
        {
            // SpecialName (get_/set_ 等) はスキップ
            if (group.Any(m => m.IsSpecialName)) continue;

            // ランタイム実装メソッド (デリゲートの Invoke/BeginInvoke/EndInvoke 等) はスキップ
            if (group.Any(m => m.IsRuntime)) continue;

            // モジュール外インターフェイス (IDisposable, IEnumerable 等) を実装する
            // メソッドはリネーム不可（CLRが名前でマッチングするため）
            if (ImplementsExternalInterface(group, typeByFullName)) continue;

            var newName = context.NameGenerator.Next();
            foreach (var method in group)
            {
                var oldName = method.Name.String;
                context.MemberRenameHistory[(method.DeclaringType, oldName)] = newName;
                sameModuleRenames[(method.DeclaringType, oldName)] = newName;
                context.Logger.Verbose($"virtual グループリネーム: {method.DeclaringType.Name}.{oldName} -> {newName}");
                method.Name = newName;
                result.RenamedSymbols++;
            }
            renamedGroups++;
        }

        if (renamedGroups > 0)
            context.Logger.Info($"virtual メソッドグループリネーム: {renamedGroups} グループ");
    }

    /// <summary>
    /// グループ内のメソッドが、モジュール外のインターフェイスを実装しているかチェックする。
    /// IDisposable.Dispose, IEnumerable.GetEnumerator 等は名前でマッチングされるため
    /// リネームすると実行時エラーになる。
    /// </summary>
    private static bool ImplementsExternalInterface(
        List<MethodDef> group,
        Dictionary<string, TypeDef> typeByFullName)
    {
        var methodName = group[0].Name.String;
        var methodSig = group[0].MethodSig;

        foreach (var method in group)
        {
            var type = method.DeclaringType;
            if (type == null) continue;

            // 型が実装する全インターフェイスをチェック
            foreach (var iface in type.Interfaces)
            {
                var ifaceFullName = iface.Interface.FullName;

                // モジュール内のインターフェイスなら OK（グループ内でまとめてリネーム可能）
                if (typeByFullName.ContainsKey(ifaceFullName)) continue;

                // モジュール外のインターフェイスに同名メソッドがあればリネーム不可
                var ifaceTypeDef = iface.Interface.ResolveTypeDef();
                if (ifaceTypeDef != null)
                {
                    if (ifaceTypeDef.Methods.Any(m =>
                        m.Name == methodName &&
                        new SigComparer().Equals(m.MethodSig, methodSig)))
                        return true;
                }
                else
                {
                    // 解決できない外部インターフェイスの場合、
                    // 安全のため名前が一般的なインターフェイスメソッドならスキップ
                    // (Resolve 失敗 = BCLや未参照アセンブリ)
                    return true;
                }
            }

            // 基底型がモジュール外にある場合、基底の virtual を override している可能性
            var baseRef = type.BaseType;
            if (baseRef != null && !typeByFullName.ContainsKey(baseRef.FullName))
            {
                // モジュール外基底型の virtual メソッドを override している場合はスキップ
                var baseTypeDef = baseRef.ResolveTypeDef();
                if (baseTypeDef != null)
                {
                    if (baseTypeDef.Methods.Any(m =>
                        m.IsVirtual && m.Name == methodName &&
                        new SigComparer().Equals(m.MethodSig, methodSig)))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// virtual メソッドの最上位の基底定義を探す。
    /// 同一モジュール内の基底型チェーンを辿り、同名・同シグネチャのメソッドを見つける。
    /// </summary>
    private static MethodDef FindVirtualRoot(
        MethodDef method, TypeDef declaringType,
        Dictionary<string, TypeDef> typeByFullName)
    {
        var current = method;
        var currentType = declaringType;

        while (true)
        {
            var baseTypeRef = currentType.BaseType;
            if (baseTypeRef == null) break;

            var baseFullName = baseTypeRef.FullName;
            if (!typeByFullName.TryGetValue(baseFullName, out var baseType)) break;

            var baseMethod = baseType.Methods.FirstOrDefault(m =>
                m.IsVirtual && m.Name == current.Name &&
                new SigComparer().Equals(m.MethodSig, current.MethodSig));

            if (baseMethod == null)
            {
                // インターフェイス実装の場合、インターフェイスも探す
                foreach (var iface in baseType.Interfaces)
                {
                    var ifaceFullName = iface.Interface.FullName;
                    if (!typeByFullName.TryGetValue(ifaceFullName, out var ifaceType)) continue;

                    var ifaceMethod = ifaceType.Methods.FirstOrDefault(m =>
                        m.IsVirtual && m.Name == current.Name &&
                        new SigComparer().Equals(m.MethodSig, current.MethodSig));
                    if (ifaceMethod != null)
                    {
                        current = ifaceMethod;
                        currentType = ifaceType;
                        break;
                    }
                }
                break;
            }

            current = baseMethod;
            currentType = baseType;
        }

        // 宣言型自身が実装するインターフェイスも確認
        foreach (var iface in declaringType.Interfaces)
        {
            var ifaceFullName = iface.Interface.FullName;
            if (!typeByFullName.TryGetValue(ifaceFullName, out var ifaceType)) continue;

            var ifaceMethod = ifaceType.Methods.FirstOrDefault(m =>
                m.IsVirtual && m.Name == method.Name &&
                new SigComparer().Equals(m.MethodSig, method.MethodSig));
            if (ifaceMethod != null && ifaceMethod != current)
            {
                // インターフェイスの方がより上位
                return ifaceMethod;
            }
        }

        return current;
    }

    /// <summary>
    /// virtual プロパティをインターフェイス定義 + 全実装でグループ化してリネームする。
    /// </summary>
    private static void RenameVirtualPropertyGroups(
        ModuleDef module, ObfuscationContext context, ObfuscationResult result,
        Dictionary<(TypeDef, string), string> sameModuleRenames)
    {
        var allTypes = module.GetTypes().ToList();
        var typeByFullName = new Dictionary<string, TypeDef>();
        foreach (var t in allTypes)
            typeByFullName[t.FullName] = t;

        // プロパティをグループ化: (インターフェイス型, プロパティ名) → List<(TypeDef, PropertyDef)>
        var groups = new Dictionary<(TypeDef, string), List<(TypeDef type, PropertyDef prop)>>();

        foreach (var type in allTypes)
        {
            foreach (var prop in type.Properties)
            {
                var getter = prop.GetMethod;
                var setter = prop.SetMethod;
                var isVirtual = (getter?.IsVirtual == true) || (setter?.IsVirtual == true);
                if (!isVirtual) continue;

                // インターフェイスのルートを探す
                var rootType = FindPropertyInterfaceRoot(type, prop.Name, typeByFullName);
                if (rootType == null) continue;

                var key = (rootType, prop.Name.String);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<(TypeDef, PropertyDef)>();
                    groups[key] = group;
                }
                group.Add((type, prop));
            }
        }

        var renamedGroups = 0;
        foreach (var (key, group) in groups)
        {
            var (rootType, _) = key;
            // ルートがモジュール外ならスキップ
            if (!typeByFullName.ContainsKey(rootType.FullName)) continue;

            // 外部インターフェイス/基底型チェック
            if (group.Any(g => HasExternalVirtualProperty(g.type, g.prop.Name, typeByFullName)))
                continue;

            var newName = context.NameGenerator.Next();
            foreach (var (type, prop) in group)
            {
                context.Logger.Verbose($"virtual プロパティリネーム: {type.Name}.{prop.Name} -> {newName}");
                prop.Name = newName;
                result.RenamedSymbols++;

                if (prop.GetMethod != null)
                {
                    var oldName = prop.GetMethod.Name.String;
                    var getterName = "get_" + newName;
                    context.MemberRenameHistory[(type, oldName)] = getterName;
                    sameModuleRenames[(type, oldName)] = getterName;
                    prop.GetMethod.Name = getterName;
                }
                if (prop.SetMethod != null)
                {
                    var oldName = prop.SetMethod.Name.String;
                    var setterName = "set_" + newName;
                    context.MemberRenameHistory[(type, oldName)] = setterName;
                    sameModuleRenames[(type, oldName)] = setterName;
                    prop.SetMethod.Name = setterName;
                }
            }
            renamedGroups++;
        }

        if (renamedGroups > 0)
            context.Logger.Info($"virtual プロパティグループリネーム: {renamedGroups} グループ");
    }

    /// <summary>
    /// virtual イベントをインターフェイス定義 + 全実装でグループ化してリネームする。
    /// </summary>
    private static void RenameVirtualEventGroups(
        ModuleDef module, ObfuscationContext context, ObfuscationResult result,
        Dictionary<(TypeDef, string), string> sameModuleRenames)
    {
        var allTypes = module.GetTypes().ToList();
        var typeByFullName = new Dictionary<string, TypeDef>();
        foreach (var t in allTypes)
            typeByFullName[t.FullName] = t;

        var groups = new Dictionary<(TypeDef, string), List<(TypeDef type, EventDef evt)>>();

        foreach (var type in allTypes)
        {
            foreach (var evt in type.Events)
            {
                var isVirtual = (evt.AddMethod?.IsVirtual == true) || (evt.RemoveMethod?.IsVirtual == true);
                if (!isVirtual) continue;

                var rootType = FindEventInterfaceRoot(type, evt.Name, typeByFullName);
                if (rootType == null) continue;

                var key = (rootType, evt.Name.String);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<(TypeDef, EventDef)>();
                    groups[key] = group;
                }
                group.Add((type, evt));
            }
        }

        var renamedGroups = 0;
        foreach (var (key, group) in groups)
        {
            var (rootType, _) = key;
            if (!typeByFullName.ContainsKey(rootType.FullName)) continue;

            if (group.Any(g => HasExternalVirtualEvent(g.type, g.evt.Name, typeByFullName)))
                continue;

            var newName = context.NameGenerator.Next();
            foreach (var (type, evt) in group)
            {
                context.Logger.Verbose($"virtual イベントリネーム: {type.Name}.{evt.Name} -> {newName}");
                evt.Name = newName;
                result.RenamedSymbols++;

                if (evt.AddMethod != null)
                {
                    var oldName = evt.AddMethod.Name.String;
                    var addName = "add_" + newName;
                    context.MemberRenameHistory[(type, oldName)] = addName;
                    sameModuleRenames[(type, oldName)] = addName;
                    evt.AddMethod.Name = addName;
                }
                if (evt.RemoveMethod != null)
                {
                    var oldName = evt.RemoveMethod.Name.String;
                    var removeName = "remove_" + newName;
                    context.MemberRenameHistory[(type, oldName)] = removeName;
                    sameModuleRenames[(type, oldName)] = removeName;
                    evt.RemoveMethod.Name = removeName;
                }
                if (evt.InvokeMethod != null)
                {
                    var oldName = evt.InvokeMethod.Name.String;
                    var raiseName = "raise_" + newName;
                    context.MemberRenameHistory[(type, oldName)] = raiseName;
                    sameModuleRenames[(type, oldName)] = raiseName;
                    evt.InvokeMethod.Name = raiseName;
                }
            }
            renamedGroups++;
        }

        if (renamedGroups > 0)
            context.Logger.Info($"virtual イベントグループリネーム: {renamedGroups} グループ");
    }

    private static TypeDef? FindPropertyInterfaceRoot(TypeDef type, UTF8String propName, Dictionary<string, TypeDef> typeByFullName)
    {
        // 型自身がインターフェイスならそれがルート
        if (type.IsInterface) return type;

        // 実装するモジュール内インターフェイスに同名プロパティがあるか
        foreach (var iface in type.Interfaces)
        {
            if (!typeByFullName.TryGetValue(iface.Interface.FullName, out var ifaceType)) continue;
            if (ifaceType.Properties.Any(p => p.Name == propName))
                return ifaceType;
        }

        // 基底型を辿る
        if (type.BaseType != null && typeByFullName.TryGetValue(type.BaseType.FullName, out var baseType))
            return FindPropertyInterfaceRoot(baseType, propName, typeByFullName);

        return type; // ルートが見つからなければ自分自身
    }

    private static TypeDef? FindEventInterfaceRoot(TypeDef type, UTF8String evtName, Dictionary<string, TypeDef> typeByFullName)
    {
        if (type.IsInterface) return type;

        foreach (var iface in type.Interfaces)
        {
            if (!typeByFullName.TryGetValue(iface.Interface.FullName, out var ifaceType)) continue;
            if (ifaceType.Events.Any(e => e.Name == evtName))
                return ifaceType;
        }

        if (type.BaseType != null && typeByFullName.TryGetValue(type.BaseType.FullName, out var baseType))
            return FindEventInterfaceRoot(baseType, evtName, typeByFullName);

        return type;
    }

    private static bool HasExternalVirtualProperty(TypeDef type, UTF8String propName, Dictionary<string, TypeDef> typeByFullName)
    {
        foreach (var iface in type.Interfaces)
        {
            if (typeByFullName.ContainsKey(iface.Interface.FullName)) continue;
            // 外部インターフェイスに同名プロパティがあればスキップ
            var ifaceTypeDef = iface.Interface.ResolveTypeDef();
            if (ifaceTypeDef == null) return true; // 解決不能 → 安全のためスキップ
            if (ifaceTypeDef.Properties.Any(p => p.Name == propName)) return true;
        }
        if (type.BaseType != null && !typeByFullName.ContainsKey(type.BaseType.FullName))
        {
            var baseTypeDef = type.BaseType.ResolveTypeDef();
            if (baseTypeDef?.Properties.Any(p => p.Name == propName) == true) return true;
        }
        return false;
    }

    private static bool HasExternalVirtualEvent(TypeDef type, UTF8String evtName, Dictionary<string, TypeDef> typeByFullName)
    {
        foreach (var iface in type.Interfaces)
        {
            if (typeByFullName.ContainsKey(iface.Interface.FullName)) continue;
            var ifaceTypeDef = iface.Interface.ResolveTypeDef();
            if (ifaceTypeDef == null) return true;
            if (ifaceTypeDef.Events.Any(e => e.Name == evtName)) return true;
        }
        if (type.BaseType != null && !typeByFullName.ContainsKey(type.BaseType.FullName))
        {
            var baseTypeDef = type.BaseType.ResolveTypeDef();
            if (baseTypeDef?.Events.Any(e => e.Name == evtName) == true) return true;
        }
        return false;
    }

    private static void RenameParameters(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var count = 0;
        foreach (var type in module.GetTypes())
        {
            if (type.Name == "<Module>") continue;

            foreach (var method in type.Methods)
            {
                if (method.Parameters == null) continue;

                foreach (var param in method.Parameters)
                {
                    // 隠しパラメーター (this等) はスキップ
                    if (param.IsHiddenThisParameter) continue;
                    if (string.IsNullOrEmpty(param.Name)) continue;

                    param.Name = context.NameGenerator.Next();
                    count++;
                }
            }
        }

        if (count > 0)
            context.Logger.Info($"パラメーター名難読化: {count} 件");
    }

    private static void FixSameModuleMemberRefs(
        ModuleDef module,
        Dictionary<(TypeDef type, string oldName), string> renames,
        ObfuscationContext context)
    {
        var fixedCount = 0;
        var processed = new HashSet<MemberRef>();

        void TryFix(MemberRef mr)
        {
            if (!processed.Add(mr)) return;

            // MemberRef.Class から TypeDef を抽出
            // TypeDef: 直接参照
            // TypeSpec: ジェネリック型インスタンス化 (例: LockFreeQueue<T>) → ベース TypeDef を取得
            var td = mr.Class switch
            {
                TypeDef directTd => directTd,
                TypeSpec ts => ExtractTypeDefFromTypeSig(ts.TypeSig),
                _ => null
            };
            if (td == null) return;

            var key = (td, mr.Name.String);
            if (renames.TryGetValue(key, out var newName))
            {
                context.Logger.Verbose($"同一モジュール MemberRef 修正: {td.Name}.{mr.Name} -> {newName}");
                mr.Name = newName;
                fixedCount++;
            }
        }

        foreach (var mr in module.GetMemberRefs())
            TryFix(mr);

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.Operand is MemberRef mr)
                        TryFix(mr);
                }
            }
        }

        if (fixedCount > 0)
            context.Logger.Info($"同一モジュール MemberRef 修正: {fixedCount} 件");
    }

    private void ProcessType(TypeDef type, ObfuscationContext context, ObfuscationResult result,
        bool renamePublic, Dictionary<(TypeDef, string), string> sameModuleRenames)
    {
        var options = context.Options;

        var enableRenameTypes = renamePublic || options.EnableRenameTypes;
        var enableRenameFields = renamePublic || options.EnableRenameFields;
        var enableRenameMethods = renamePublic || options.EnableRenameMethods;
        var enableRenameProperties = renamePublic || options.EnableRenameProperties;

        foreach (var nested in type.NestedTypes)
            ProcessType(nested, context, result, renamePublic, sameModuleRenames);

        if ((type.IsPublic || type.IsNestedPublic) && !renamePublic)
            return;

        if (IsCompilerGenerated(type))
            return;

        // フィールドの名前変更
        if (enableRenameFields)
        {
            foreach (var field in type.Fields)
            {
                if (field.IsPublic && !renamePublic)
                    continue;

                var oldName = field.Name.String;
                var newName = context.NameGenerator.Next();
                context.NameMap[field.FullName] = newName;
                // TypeDef オブジェクト参照でリネーム履歴を記録（クロスアセンブリ修正用）
                context.MemberRenameHistory[(type, oldName)] = newName;
                sameModuleRenames[(type, oldName)] = newName;
                context.Logger.Verbose($"フィールド: {oldName} -> {newName}");
                field.Name = newName;
                result.RenamedSymbols++;
            }
        }

        // メソッドの名前変更
        if (enableRenameMethods)
        {
            foreach (var method in type.Methods)
            {
                if (ShouldSkipMethod(method, renamePublic))
                    continue;

                var oldName = method.Name.String;
                var newName = context.NameGenerator.Next();
                context.NameMap[method.FullName] = newName;
                context.MemberRenameHistory[(type, oldName)] = newName;
                sameModuleRenames[(type, oldName)] = newName;
                context.Logger.Verbose($"メソッド: {oldName} -> {newName}");
                method.Name = newName;
                result.RenamedSymbols++;
            }
        }

        // プロパティの名前変更（getter/setterメソッド名も連動）
        if (enableRenameProperties)
        {
            foreach (var property in type.Properties)
            {
                var isPublic = property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true;
                if (isPublic && !renamePublic)
                    continue;

                var newName = context.NameGenerator.Next();
                context.Logger.Verbose($"プロパティ: {property.Name} -> {newName}");
                property.Name = newName;
                result.RenamedSymbols++;

                if (property.GetMethod != null && (!property.GetMethod.IsPublic || renamePublic))
                {
                    if (!property.GetMethod.IsVirtual && !property.GetMethod.HasOverrides)
                    {
                        var oldGetterName = property.GetMethod.Name.String;
                        var getterName = "get_" + newName;
                        context.NameMap[property.GetMethod.FullName] = getterName;
                        context.MemberRenameHistory[(type, oldGetterName)] = getterName;
                        sameModuleRenames[(type, oldGetterName)] = getterName;
                        property.GetMethod.Name = getterName;
                    }
                }
                if (property.SetMethod != null && (!property.SetMethod.IsPublic || renamePublic))
                {
                    if (!property.SetMethod.IsVirtual && !property.SetMethod.HasOverrides)
                    {
                        var oldSetterName = property.SetMethod.Name.String;
                        var setterName = "set_" + newName;
                        context.NameMap[property.SetMethod.FullName] = setterName;
                        context.MemberRenameHistory[(type, oldSetterName)] = setterName;
                        sameModuleRenames[(type, oldSetterName)] = setterName;
                        property.SetMethod.Name = setterName;
                    }
                }
            }
        }

        // イベントの名前変更（add/remove/raise メソッド名も連動）
        if (enableRenameProperties) // プロパティと同じカテゴリで制御
        {
            foreach (var evt in type.Events)
            {
                var isPublic = evt.AddMethod?.IsPublic == true || evt.RemoveMethod?.IsPublic == true;
                if (isPublic && !renamePublic)
                    continue;

                // アクセサが virtual の場合、外部インターフェイス実装ならスキップ
                var accessors = new[] { evt.AddMethod, evt.RemoveMethod, evt.InvokeMethod }
                    .Where(m => m != null).ToList();
                if (accessors.Any(m => m!.IsVirtual || m.HasOverrides))
                    continue;

                var newName = context.NameGenerator.Next();
                context.Logger.Verbose($"イベント: {evt.Name} -> {newName}");
                evt.Name = newName;
                result.RenamedSymbols++;

                if (evt.AddMethod != null && (!evt.AddMethod.IsPublic || renamePublic))
                {
                    var oldAddName = evt.AddMethod.Name.String;
                    var addName = "add_" + newName;
                    context.NameMap[evt.AddMethod.FullName] = addName;
                    context.MemberRenameHistory[(type, oldAddName)] = addName;
                    sameModuleRenames[(type, oldAddName)] = addName;
                    evt.AddMethod.Name = addName;
                }
                if (evt.RemoveMethod != null && (!evt.RemoveMethod.IsPublic || renamePublic))
                {
                    var oldRemoveName = evt.RemoveMethod.Name.String;
                    var removeName = "remove_" + newName;
                    context.NameMap[evt.RemoveMethod.FullName] = removeName;
                    context.MemberRenameHistory[(type, oldRemoveName)] = removeName;
                    sameModuleRenames[(type, oldRemoveName)] = removeName;
                    evt.RemoveMethod.Name = removeName;
                }
                if (evt.InvokeMethod != null && (!evt.InvokeMethod.IsPublic || renamePublic))
                {
                    var oldRaiseName = evt.InvokeMethod.Name.String;
                    var raiseName = "raise_" + newName;
                    context.NameMap[evt.InvokeMethod.FullName] = raiseName;
                    context.MemberRenameHistory[(type, oldRaiseName)] = raiseName;
                    sameModuleRenames[(type, oldRaiseName)] = raiseName;
                    evt.InvokeMethod.Name = raiseName;
                }
            }
        }

        // 型自体の名前変更
        if (enableRenameTypes)
        {
            var newTypeName = context.NameGenerator.Next();
            context.NameMap[type.FullName] = newTypeName;
            context.Logger.Verbose($"型: {type.Name} -> {newTypeName}");
            type.Name = newTypeName;
            result.RenamedSymbols++;
        }
    }

    /// <summary>
    /// TypeSig から TypeDef を抽出する。
    /// GenericInstSig (ジェネリック型インスタンス化) や ModifierSig (modopt/modreq) を辿る。
    /// </summary>
    private static TypeDef? ExtractTypeDefFromTypeSig(TypeSig? typeSig)
    {
        while (typeSig is ModifierSig modSig)
            typeSig = modSig.Next;
        if (typeSig is GenericInstSig genSig)
            typeSig = genSig.GenericType;
        return (typeSig as ClassOrValueTypeSig)?.TypeDefOrRef as TypeDef;
    }

    private static bool ShouldSkipMethod(MethodDef method, bool renamePublic)
    {
        if (method.IsPublic && !renamePublic) return true;
        if (method.IsConstructor) return true;
        if (method.Name == "Main") return true;
        if (method.IsVirtual) return true;
        if (method.HasOverrides) return true;
        if (method.IsSpecialName) return true;
        if (method.ImplMap != null) return true;
        if (method.IsPinvokeImpl) return true;
        // ランタイム実装メソッド (デリゲートの Invoke 等)
        if (method.IsRuntime) return true;
        return false;
    }

    private static bool IsCompilerGenerated(TypeDef type)
    {
        var name = type.Name.String;
        if (name.StartsWith("<>f__AnonymousType"))
            return true;
        if (name.StartsWith("<>c__DisplayClass") || name == "<>c")
            return true;
        if (name.Contains(">d__"))
            return true;
        if (name.Contains(">e__"))
            return true;
        if (type.CustomAttributes.Any(a => a.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;
        return false;
    }
}
