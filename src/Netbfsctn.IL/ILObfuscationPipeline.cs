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
            var outputPath = options.OutputPath
                ?? BuildDefaultOutputPath(options.InputPath);

            logger.Info($"アセンブリを読み込み中: {options.InputPath}");
            using var module = ModuleDefMD.Load(options.InputPath);

            var isMixedMode = !module.IsILOnly;
            if (isMixedMode)
                logger.Info("混合モード (C++/CLI) アセンブリを検出しました。NativeModuleWriter を使用します。");

            var result = new ObfuscationResult { Success = true, OutputPath = outputPath };

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
            if (options.EnableAntiTampering)
                techniques.Add(new ILAntiTampering());

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

            // マッピングファイルのポスト処理
            if (options.EnableMappingFile || options.EnableRename)
            {
                if (options.EnableMappingFile)
                {
                    var mappingPath = options.MappingFilePath
                        ?? $"{outputPath}.map.json";
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

    private static string BuildDefaultOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{name}.obfuscated{ext}");
    }
}
