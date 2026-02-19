using Mono.Cecil;
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
            var readerParams = new ReaderParameters { ReadWrite = false };
            using var assembly = AssemblyDefinition.ReadAssembly(options.InputPath, readerParams);
            var module = assembly.MainModule;

            var result = new ObfuscationResult { Success = true, OutputPath = outputPath };

            var techniques = new List<IObfuscationTechnique<ModuleDefinition>>();

            if (options.EnableRename)
                techniques.Add(new ILNameObfuscator());
            if (options.EnableStringEncryption)
                techniques.Add(new ILStringEncryptor());
            if (options.EnableControlFlow)
                techniques.Add(new ILControlFlowObfuscator());
            if (options.EnableDeadCode)
                techniques.Add(new ILDeadCodeInserter());

            foreach (var technique in techniques)
            {
                logger.Info($"適用中: {technique.Name}");
                technique.Apply(module, context, result);
            }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            logger.Info($"保存中: {outputPath}");
            assembly.Write(outputPath);

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
