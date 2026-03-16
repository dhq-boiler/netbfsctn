using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;
using Netbfsctn.IL.Techniques;

namespace Netbfsctn.IL;

public class ILObfuscationPipeline : IObfuscationPipeline
{
    public ObfuscationResult Execute(ObfuscationContext context)
    {
        var options = context.Options;
        var logger = context.Logger;

        try
        {
            // メインアセンブリを処理
            var mainOutputPath = options.OutputPath
                ?? BuildDefaultOutputPath(options.InputPath);
            var result = ExecuteSingle(context, options.InputPath, mainOutputPath);
            if (!result.Success)
                return result;

            // 追加アセンブリを同じコンテキスト（NameMap/NameGenerator共有）で処理
            for (var i = 0; i < options.AdditionalInputPaths.Length; i++)
            {
                var additionalInput = options.AdditionalInputPaths[i];
                var additionalOutput = i < options.AdditionalOutputPaths.Length
                    ? options.AdditionalOutputPaths[i]
                    : BuildDefaultOutputPath(additionalInput);

                logger.Info($"追加アセンブリを処理中: {additionalInput}");
                var additionalResult = ExecuteSingle(context, additionalInput, additionalOutput);
                if (!additionalResult.Success)
                {
                    logger.Error($"追加アセンブリの処理に失敗: {additionalInput} - {additionalResult.ErrorMessage}");
                    return additionalResult;
                }

                // 結果を集約
                result.RenamedSymbols += additionalResult.RenamedSymbols;
                result.EncryptedStrings += additionalResult.EncryptedStrings;
                result.ObfuscatedMethods += additionalResult.ObfuscatedMethods;
                result.InsertedDeadCodeBlocks += additionalResult.InsertedDeadCodeBlocks;
                result.EncryptedMethodBodies += additionalResult.EncryptedMethodBodies;
                result.HiddenMethodCalls += additionalResult.HiddenMethodCalls;
                result.ProtectedResources += additionalResult.ProtectedResources;
                result.VirtualizedMethods += additionalResult.VirtualizedMethods;

                logger.Success($"追加アセンブリ完了: {additionalOutput}");
            }

            // マッピングファイルのポスト処理（全アセンブリ処理後に一括出力）
            if (options.EnableMappingFile || options.EnableRename)
            {
                if (options.EnableMappingFile)
                {
                    var mappingPath = options.MappingFilePath
                        ?? $"{mainOutputPath}.map.json";
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
    }

    private ObfuscationResult ExecuteSingle(ObfuscationContext context, string inputPath, string outputPath)
    {
        var options = context.Options;
        var logger = context.Logger;

        logger.Info($"アセンブリを読み込み中: {inputPath}");
        using var module = ModuleDefMD.Load(inputPath);

        var isMixedMode = !module.IsILOnly;
        if (isMixedMode)
            logger.Info("混合モード (C++/CLI) アセンブリを検出しました。NativeModuleWriter を使用します。");

        var result = new ObfuscationResult { Success = true, OutputPath = outputPath };

        var techniques = BuildTechniqueList(options);

        foreach (var technique in techniques)
        {
            logger.Info($"適用中: {technique.Name}");
            technique.Apply(module, context, result);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 難読化後のメソッドでブランチ最適化
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

        // KeepOldMaxStack が設定されたメソッド（制御フロー難読化済み）の maxStack を
        // 最終命令列から正確に再計算する。後続テクニックが命令を追加・変更している可能性があるため、
        // 全テクニック適用後にここで計算する必要がある。
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (!method.Body.KeepOldMaxStack) continue;
                method.Body.MaxStack = (ushort)CalculateMaxStack(method.Body, method);
            }
        }

        logger.Info($"保存中: {outputPath}");
        WriteModule(module, outputPath, isMixedMode);

        return result;
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
