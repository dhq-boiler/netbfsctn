using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;
using Netbfsctn.Core.Xaml;
using Netbfsctn.IL.Techniques;

namespace Netbfsctn.IL;

public class ILObfuscationPipeline : IObfuscationPipeline
{
    public ObfuscationResult Execute(ObfuscationContext context)
    {
        var options = context.Options;
        var logger = context.Logger;

        var modules = new List<ModuleDefMD>();

        try
        {
            // ── Phase 1: 全モジュールをロード ──
            var entries = new List<(string inputPath, string outputPath, bool isMixedMode)>();

            var mainOutput = options.OutputPath ?? BuildDefaultOutputPath(options.InputPath);
            logger.Info($"アセンブリを読み込み中: {options.InputPath}");
            var mainModule = ModuleDefMD.Load(options.InputPath);
            modules.Add(mainModule);
            var mainMixed = !mainModule.IsILOnly;
            if (mainMixed)
                logger.Info("混合モード (C++/CLI) アセンブリを検出しました。NativeModuleWriter を使用します。");
            entries.Add((options.InputPath, mainOutput, mainMixed));

            for (var i = 0; i < options.AdditionalInputPaths.Length; i++)
            {
                var addInput = options.AdditionalInputPaths[i];
                var addOutput = i < options.AdditionalOutputPaths.Length
                    ? options.AdditionalOutputPaths[i]
                    : BuildDefaultOutputPath(addInput);

                logger.Info($"追加アセンブリを読み込み中: {addInput}");
                var addModule = ModuleDefMD.Load(addInput);
                modules.Add(addModule);
                var addMixed = !addModule.IsILOnly;
                if (addMixed)
                    logger.Info("混合モード (C++/CLI) アセンブリを検出しました。NativeModuleWriter を使用します。");
                entries.Add((addInput, addOutput, addMixed));
            }

            // WPF アセンブリ検出 (型リネーム自動スキップ用)
            foreach (var module in modules)
            {
                var moduleName = module.Assembly?.Name?.String ?? "";
                if (IsWpfAssembly(module))
                {
                    context.WpfModuleNames.Add(moduleName);
                    logger.Info($"WPF アセンブリを検出: {moduleName} (BAML 互換のため型リネームを自動スキップ)");
                }
            }

            // EnableRenamePublic の除外リストを構築
            if (options.EnableRenamePublic)
            {
                // 手動除外
                foreach (var name in options.ExcludeRenamePublic)
                    context.ExcludeRenamePublicModules.Add(name);

                // WPF モジュールの public リネーム除外:
                // --xaml-dir 指定時は XAML 解析で精密に除外するため、自動除外しない
                // --xaml-dir 未指定時は安全のため全 public を除外 (従来動作)
                if (options.XamlDirectories.Length == 0)
                {
                    foreach (var wpfName in context.WpfModuleNames)
                        context.ExcludeRenamePublicModules.Add(wpfName);
                }

                foreach (var module in modules)
                {
                    var moduleName = module.Assembly?.Name?.String ?? "";
                    if (context.ExcludeRenamePublicModules.Contains(moduleName))
                    {
                        logger.Info($"public リネーム除外: {moduleName}");
                    }
                    else
                    {
                        logger.Info($"public リネーム対象: {moduleName}");
                    }
                }
            }

            // ── XAML 解析: バインディング参照名を収集 ──
            if (options.XamlDirectories.Length > 0)
            {
                logger.Info("XAML ソースディレクトリを解析中...");
                context.XamlAnalysis = XamlBindingAnalyzer.Analyze(options.XamlDirectories, logger);
                var xa = context.XamlAnalysis;
                logger.Info($"XAML 解析完了: 型={xa.ReferencedTypes.Count}, プロパティ={xa.BoundPropertyNames.Count}, イベントハンドラ={xa.EventHandlerNames.Count}, enum値={xa.ReferencedEnumValues.Count}");
            }

            // ── Phase 2a: リネーム前に TypeRef → TypeDef を事前解決 ──
            // リネーム後は名前ベースの Resolve() が失敗するため、
            // 名前が一致している今のうちに TypeRef → TypeDef を解決しキャッシュする。
            var typeRefCache = new Dictionary<TypeRef, TypeDef>();
            var hasRenamePublicTargets = options.EnableRenamePublic
                && modules.Any(m => !context.ExcludeRenamePublicModules.Contains(m.Assembly?.Name?.String ?? ""));
            if (modules.Count > 1 && hasRenamePublicTargets && options.EnableRename)
            {
                logger.Info("クロスアセンブリ TypeRef を事前解決中...");
                PreResolveTypeRefs(modules, typeRefCache, logger);
            }

            // ── Phase 2b: 全モジュールにテクニックを適用 ──
            var result = new ObfuscationResult { Success = true, OutputPath = mainOutput };
            var techniques = BuildTechniqueList(options);

            foreach (var module in modules)
            {
                logger.Info($"アセンブリを処理中: {module.Name}");
                foreach (var technique in techniques)
                {
                    logger.Info($"  適用中: {technique.Name}");
                    technique.Apply(module, context, result);
                }
            }

            // ── Phase 3: TypeRef を同期し、MemberRef を MemberRenameHistory から修正 ──
            if (typeRefCache.Count > 0)
            {
                logger.Info("クロスアセンブリ参照を同期中...");
                SyncCrossAssemblyRefs(modules, typeRefCache, context, logger);
            }

            // ── Phase 4: 後処理（ブランチ最適化・maxStack再計算）──
            foreach (var module in modules)
            {
                foreach (var type in module.GetTypes())
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        var body = method.Body;
                        body.SimplifyBranches();
                        body.OptimizeBranches();
                    }
                }

                foreach (var type in module.GetTypes())
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        if (!method.Body.KeepOldMaxStack) continue;
                        method.Body.MaxStack = (ushort)CalculateMaxStack(method.Body, method);
                    }
                }
            }

            // ── Phase 5: 全モジュールを書き出し ──
            for (var i = 0; i < modules.Count; i++)
            {
                var (_, outputPath, isMixedMode) = entries[i];
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                logger.Info($"保存中: {outputPath}");
                WriteModule(modules[i], outputPath, isMixedMode);

                if (i > 0)
                    logger.Success($"追加アセンブリ完了: {outputPath}");
            }

            // ── Phase 6: マッピングファイル出力 ──
            if (options.EnableMappingFile || options.EnableRename)
            {
                if (options.EnableMappingFile)
                {
                    var mappingPath = options.MappingFilePath
                        ?? $"{mainOutput}.map.json";
                    var json = JsonSerializer.Serialize(context.NameMap,
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(mappingPath, json);
                    result.MappingFilePath = mappingPath;
                    logger.Info($"マッピングファイル出力: {mappingPath}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex.Message);
            return new ObfuscationResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            foreach (var module in modules)
                module.Dispose();
        }
    }

    /// <summary>
    /// WPF アセンブリかどうかを判定する。
    /// PresentationFramework または WindowsBase への参照があれば WPF とみなす。
    /// </summary>
    private static bool IsWpfAssembly(ModuleDefMD module)
    {
        foreach (var asmRef in module.GetAssemblyRefs())
        {
            var name = asmRef.Name.String;
            if (name == "PresentationFramework" || name == "PresentationCore")
                return true;
        }
        return false;
    }

    /// <summary>
    /// リネーム前に全モジュール間の TypeRef → TypeDef を解決しキャッシュする。
    /// </summary>
    private static void PreResolveTypeRefs(
        List<ModuleDefMD> modules,
        Dictionary<TypeRef, TypeDef> cache,
        ObfuscationLogger logger)
    {
        var asmResolver = new AssemblyResolver();
        var modCtx = new ModuleContext(asmResolver);
        asmResolver.DefaultModuleContext = modCtx;

        var batchAssemblyNames = new HashSet<string>();
        foreach (var module in modules)
        {
            module.Context = modCtx;
            if (module.Assembly != null)
            {
                asmResolver.AddToCache(module.Assembly);
                batchAssemblyNames.Add(module.Assembly.Name.String);
            }
        }

        foreach (var module in modules)
        {
            var myAssemblyName = module.Assembly?.Name?.String ?? "";

            foreach (var typeRef in module.GetTypeRefs())
            {
                var asmName = GetReferencedAssemblyName(typeRef);
                if (asmName == null || !batchAssemblyNames.Contains(asmName) || asmName == myAssemblyName)
                    continue;

                var resolved = typeRef.Resolve();
                if (resolved != null)
                    cache[typeRef] = resolved;
            }
        }

        logger.Info($"TypeRef 事前解決完了: {cache.Count} 件");
    }

    /// <summary>
    /// リネーム後にクロスアセンブリ参照を同期する。
    /// TypeRef はキャッシュ済み TypeDef から名前を同期。
    /// MemberRef は TypeRef→TypeDef + 基底型チェーン走査 + MemberRenameHistory で名前を取得。
    /// </summary>
    private static void SyncCrossAssemblyRefs(
        List<ModuleDefMD> modules,
        Dictionary<TypeRef, TypeDef> typeRefCache,
        ObfuscationContext context,
        ObfuscationLogger logger)
    {
        var fixedTypeRefs = 0;
        var fixedMemberRefs = 0;
        var missedMemberRefs = 0;

        TypeDef? FindTypeDef(IMemberRefParent? cls)
        {
            return cls switch
            {
                TypeRef tr => typeRefCache.GetValueOrDefault(tr),
                TypeSpec ts => FindTypeDefFromTypeSpec(ts, typeRefCache),
                _ => null
            };
        }

        // 基底型チェーンを辿って MemberRenameHistory を検索（パラメータ数付き）
        string? FindRenamedMember(TypeDef? typeDef, string memberName, int paramCount)
        {
            var current = typeDef;
            while (current != null)
            {
                var key = ((object)current, memberName, paramCount);
                if (context.MemberRenameHistory.TryGetValue(key, out var newName))
                    return newName;

                // 基底型を辿る
                var baseType = current.BaseType;
                if (baseType == null) break;

                current = baseType switch
                {
                    TypeDef td => td,
                    TypeRef tr => typeRefCache.GetValueOrDefault(tr) ?? tr.Resolve(),
                    TypeSpec ts => FindTypeDefFromTypeSpec(ts, typeRefCache),
                    _ => null
                };
            }
            return null;
        }

        // TypeRef の名前同期（MemberRef 同期より先に行う）
        // MemberRef のシグネチャ文字列が TypeRef の名前に依存するため、
        // TypeRef を先に更新しないとシグネチャ比較が失敗する。
        foreach (var (typeRef, typeDef) in typeRefCache)
        {
            if (typeRef.Name != typeDef.Name)
            {
                logger.Verbose($"TypeRef同期: {typeRef.Name} -> {typeDef.Name}");
                typeRef.Name = typeDef.Name;
                fixedTypeRefs++;
            }
            if (typeRef.ResolutionScope is not TypeRef && typeRef.Namespace != typeDef.Namespace)
                typeRef.Namespace = typeDef.Namespace;
        }

        foreach (var module in modules)
        {
            var processed = new HashSet<MemberRef>();

            void SyncMemberRef(MemberRef mr)
            {
                if (!processed.Add(mr)) return;

                var typeDef = FindTypeDef(mr.Class);
                if (typeDef == null) return;

                // MemberRef のパラメータ数を取得（フィールドは -1）
                var paramCount = mr.MethodSig?.Params?.Count ?? -1;
                var newName = FindRenamedMember(typeDef, mr.Name.String, paramCount);
                if (newName != null)
                {
                    logger.Verbose($"MemberRef同期: {mr.Name} -> {newName} (in {module.Name})");
                    mr.Name = newName;
                    fixedMemberRefs++;
                }
                else
                {
                    // フィールド/メソッドがリネーム対象外（public でスキップ、コンストラクタ等）の場合は正常
                    // リネームされたはずなのに見つからない場合は警告
                    missedMemberRefs++;
                    logger.Verbose($"MemberRef未修正 (リネーム対象外の可能性): {typeDef.FullName}.{mr.Name} (in {module.Name})");
                }
            }

            // Pass 1: MemberRef テーブル
            foreach (var mr in module.GetMemberRefs())
                SyncMemberRef(mr);

            // Pass 2: IL 命令オペランド
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.Operand is MemberRef mr)
                            SyncMemberRef(mr);
                    }
                }
            }
        }

        logger.Info($"クロスアセンブリ参照修正完了: TypeRef={fixedTypeRefs}, MemberRef={fixedMemberRefs} (未修正={missedMemberRefs})");
    }

    private static string? GetReferencedAssemblyName(TypeRef typeRef)
    {
        var scope = typeRef.ResolutionScope;
        while (scope is TypeRef parentTypeRef)
            scope = parentTypeRef.ResolutionScope;
        return (scope as AssemblyRef)?.Name?.String;
    }

    private static TypeDef? FindTypeDefFromTypeSpec(TypeSpec typeSpec, Dictionary<TypeRef, TypeDef> cache)
    {
        var typeSig = typeSpec.TypeSig;
        while (typeSig is ModifierSig modSig)
            typeSig = modSig.Next;
        if (typeSig is GenericInstSig genSig)
            typeSig = genSig.GenericType;
        if ((typeSig as ClassOrValueTypeSig)?.TypeDefOrRef is TypeRef tr)
            return cache.GetValueOrDefault(tr);
        return null;
    }

    private static void WriteModule(ModuleDefMD module, string outputPath, bool isMixedMode)
    {
        if (isMixedMode)
        {
            var nativeOptions = new NativeModuleWriterOptions(module, optimizeImageSize: false);
            module.NativeWrite(outputPath, nativeOptions);
        }
        else
        {
            module.Write(outputPath);
        }
    }

    private static List<IObfuscationTechnique<ModuleDef>> BuildTechniqueList(ObfuscationOptions options)
    {
        var techniques = new List<IObfuscationTechnique<ModuleDef>>();

        // 既存テクニック
        if (options.EnableRename)
            techniques.Add(new ILNameObfuscator());
        if (options.EnableStringEncryption)
            techniques.Add(new ILStringEncryptor());
        if (options.EnableControlFlow)
            techniques.Add(new ILControlFlowObfuscator());
        if (options.EnableDeadCode)
            techniques.Add(new ILDeadCodeInserter());

        // 追加テクニック (適用順序に従って登録)
        if (options.EnableAntiIldasm)
            techniques.Add(new ILAntiIldasm());
        if (options.EnableAntiDebug)
            techniques.Add(new ILAntiDebug());
        if (options.EnableHideMethodCalls)
            techniques.Add(new ILHideMethodCalls());
        if (options.EnableResourceProtection)
            techniques.Add(new ILResourceProtector());
        if (options.EnableNecroBit)
            techniques.Add(new ILNecroBit());
        if (options.EnableCodeVirtualization)
            techniques.Add(new ILCodeVirtualizer());

        // 2回目の文字列暗号化: 後段の技法が注入した新規ldstrを暗号化
        var needsSecondStringPass = options.EnableStringEncryption
            && (options.EnableHideMethodCalls || options.EnableNecroBit
                || options.EnableAntiDebug);
        if (needsSecondStringPass)
            techniques.Add(new ILStringEncryptor());

        if (options.EnableAntiTampering)
            techniques.Add(new ILAntiTampering());

        return techniques;
    }

    private static string BuildDefaultOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{name}.obfuscated{ext}");
    }

    /// <summary>
    /// 制御フローグラフを走査して最大スタック深度を計算する。
    /// 分岐先にスタック深度を伝播し、固定点に達するまで反復する。
    /// </summary>
    private static int CalculateMaxStack(CilBody body, MethodDef method)
    {
        if (body.Instructions.Count == 0)
            return 0;

        var hasReturnValue = method.ReturnType != null
            && method.ReturnType.ElementType != ElementType.Void;

        // 命令インデックスマップ
        var instrToIdx = new Dictionary<Instruction, int>(body.Instructions.Count);
        for (var i = 0; i < body.Instructions.Count; i++)
            instrToIdx[body.Instructions[i]] = i;

        // 各命令の入口スタック深度 (-1 = 未到達)
        var depthAt = new int[body.Instructions.Count];
        Array.Fill(depthAt, -1);
        depthAt[0] = 0;

        // 例外ハンドラの開始点はスタック深度1 (例外オブジェクト)
        foreach (var eh in body.ExceptionHandlers)
        {
            if (eh.HandlerStart != null && instrToIdx.TryGetValue(eh.HandlerStart, out var hIdx))
                depthAt[hIdx] = Math.Max(depthAt[hIdx], 1);
            if (eh.FilterStart != null && instrToIdx.TryGetValue(eh.FilterStart, out var fIdx))
                depthAt[fIdx] = Math.Max(depthAt[fIdx], 1);
        }

        var maxDepth = 0;
        var changed = true;
        var iterations = 0;
        var maxIterations = body.Instructions.Count * 2;

        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;
            for (var i = 0; i < body.Instructions.Count; i++)
            {
                if (depthAt[i] < 0) continue;

                var instr = body.Instructions[i];
                var depth = depthAt[i];
                depth -= CalculateStackPop(instr, hasReturnValue);
                if (depth < 0) depth = 0;
                depth += CalculateStackPush(instr);
                if (depth > maxDepth) maxDepth = depth;

                // 分岐先にスタック深度を伝播
                if (instr.Operand is Instruction target && instrToIdx.TryGetValue(target, out var tIdx))
                {
                    if (depth > depthAt[tIdx])
                    {
                        depthAt[tIdx] = depth;
                        changed = true;
                    }
                }
                if (instr.Operand is Instruction[] targets)
                {
                    foreach (var t in targets)
                    {
                        if (instrToIdx.TryGetValue(t, out var sIdx) && depth > depthAt[sIdx])
                        {
                            depthAt[sIdx] = depth;
                            changed = true;
                        }
                    }
                }

                // フォールスルー
                if (instr.OpCode.FlowControl != FlowControl.Branch
                    && instr.OpCode.FlowControl != FlowControl.Return
                    && instr.OpCode.FlowControl != FlowControl.Throw)
                {
                    if (i + 1 < body.Instructions.Count && depth > depthAt[i + 1])
                    {
                        depthAt[i + 1] = depth;
                        changed = true;
                    }
                }
            }
        }

        return maxDepth;
    }

    private static MethodSig? GetMethodSig(Instruction instr)
    {
        if (instr.Operand is IMethodDefOrRef methodRef)
            return methodRef.MethodSig;
        if (instr.Operand is MethodSpec methodSpec)
            return methodSpec.Method?.MethodSig;
        return null;
    }

    private static int CalculateStackPop(Instruction instr, bool methodHasReturnValue)
    {
        if (instr.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
        {
            var sig = GetMethodSig(instr);
            if (sig != null)
            {
                var count = sig.Params.Count;
                if (sig.HasThis && instr.OpCode.Code != Code.Newobj)
                    count++;
                return count;
            }
            return 0;
        }

        if (instr.OpCode.Code == Code.Ret)
            return methodHasReturnValue ? 1 : 0;

        return instr.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1
                or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8
                or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi
                or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref
                or StackBehaviour.Popref_popi_pop1 => 3,
            _ => 0
        };
    }

    private static int CalculateStackPush(Instruction instr)
    {
        if (instr.OpCode.Code is Code.Call or Code.Callvirt)
        {
            var sig = GetMethodSig(instr);
            if (sig == null) return 0;
            return sig.RetType != null && sig.RetType.ElementType != ElementType.Void ? 1 : 0;
        }

        if (instr.OpCode.Code == Code.Newobj)
            return 1;

        return instr.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8
                or StackBehaviour.Pushr4 or StackBehaviour.Pushr8
                or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => 0
        };
    }
}
