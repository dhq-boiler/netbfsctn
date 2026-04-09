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
