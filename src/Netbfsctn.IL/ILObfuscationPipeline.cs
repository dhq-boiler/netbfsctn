using System.Text.Json;
using dnlib.DotNet;
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

        // 難読化後のメソッドでブランチ最適化と検証
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                var body = method.Body;

                // 不正な分岐ターゲットを検出
                var instrSet = new HashSet<dnlib.DotNet.Emit.Instruction>(body.Instructions);
                var hasInvalid = false;
                foreach (var instr in body.Instructions)
                {
                    if (instr.Operand is dnlib.DotNet.Emit.Instruction target && !instrSet.Contains(target))
                    {
                        hasInvalid = true;
                        break;
                    }
                    if (instr.Operand is dnlib.DotNet.Emit.Instruction[] targets)
                    {
                        foreach (var t in targets)
                        {
                            if (!instrSet.Contains(t))
                            {
                                hasInvalid = true;
                                break;
                            }
                        }
                        if (hasInvalid) break;
                    }
                }

                if (hasInvalid)
                {
                    // 不正な参照がある場合は元の maxStack を保持して書き出し
                    body.KeepOldMaxStack = true;
                    logger.Verbose($"不正な分岐参照を検出: {method.FullName} - KeepOldMaxStack で保存");
                    continue;
                }

                // maxStack を再計算させる（KeepOldMaxStack = false がデフォルト）
                body.SimplifyBranches();
                body.OptimizeBranches();
            }
        }

        logger.Info($"保存中: {outputPath}");
        if (isMixedMode)
        {
            var nativeOptions = new NativeModuleWriterOptions(module, optimizeImageSize: false);
            module.NativeWrite(outputPath, nativeOptions);
        }
        else
        {
            module.Write(outputPath);
        }

        return result;
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
}
