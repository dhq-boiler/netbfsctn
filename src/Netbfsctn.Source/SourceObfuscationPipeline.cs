using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;
using Netbfsctn.Source.Rewriters;

namespace Netbfsctn.Source;

public class SourceObfuscationPipeline : IObfuscationPipeline
{
    public ObfuscationResult Execute(ObfuscationContext context)
    {
        var options = context.Options;
        var logger = context.Logger;

        try
        {
            // 入力ファイルを収集
            var inputFiles = CollectSourceFiles(options.InputPath);
            if (inputFiles.Count == 0)
            {
                return new ObfuscationResult
                {
                    Success = false,
                    ErrorMessage = "C# ソースファイルが見つかりません"
                };
            }

            logger.Info($"{inputFiles.Count} 個のソースファイルを処理中");

            // 出力ディレクトリ
            var outputDir = options.OutputPath
                ?? BuildDefaultOutputDir(options.InputPath);
            Directory.CreateDirectory(outputDir);

            var result = new ObfuscationResult { Success = true, OutputPath = outputDir };

            // 各ファイルを解析してSyntaxTreeを構築
            var trees = new List<SyntaxTree>();
            foreach (var file in inputFiles)
            {
                var source = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(source, path: file);
                trees.Add(tree);
            }

            // コンパイルを作成してSemanticModelを得る
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            };

            // System.Runtime の参照を追加
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var runtimeRef = Path.Combine(runtimeDir, "System.Runtime.dll");
            var refList = references.ToList();
            if (File.Exists(runtimeRef))
                refList.Add(MetadataReference.CreateFromFile(runtimeRef));

            var compilation = CSharpCompilation.Create(
                "ObfuscationAnalysis",
                trees,
                refList,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // 難読化技法を順次適用
            var techniques = new List<IObfuscationTechnique<(List<SyntaxTree> Trees, CSharpCompilation Compilation)>>();

            if (options.EnableRename)
                techniques.Add(new NameObfuscationRewriter());
            if (options.EnableStringEncryption)
                techniques.Add(new StringEncryptionRewriter());
            if (options.EnableControlFlow)
                techniques.Add(new ControlFlowRewriter());
            if (options.EnableDeadCode)
                techniques.Add(new DeadCodeInsertionRewriter());

            var state = (Trees: trees, Compilation: compilation);

            foreach (var technique in techniques)
            {
                logger.Info($"適用中: {technique.Name}");
                technique.Apply(state, context, result);
            }

            // 変換結果を書き出し
            var originalCount = inputFiles.Count;
            for (var i = 0; i < originalCount; i++)
            {
                var tree = state.Trees[i];
                var originalPath = inputFiles[i];
                var relativePath = Path.GetFileName(originalPath);
                var outputPath = Path.Combine(outputDir, relativePath);

                var transformed = tree.GetRoot().NormalizeWhitespace().ToFullString();
                File.WriteAllText(outputPath, transformed);
                logger.Verbose($"出力: {outputPath}");
            }

            // ヘルパークラスが追加されている場合、それも出力
            if (state.Trees.Count > originalCount)
            {
                for (var i = originalCount; i < state.Trees.Count; i++)
                {
                    var tree = state.Trees[i];
                    var helperPath = Path.Combine(outputDir, $"__Helper{i - originalCount}.cs");
                    File.WriteAllText(helperPath, tree.GetRoot().NormalizeWhitespace().ToFullString());
                    logger.Verbose($"ヘルパー出力: {helperPath}");
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

    private static List<string> CollectSourceFiles(string path)
    {
        if (File.Exists(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return [path];

        if (Directory.Exists(path))
            return Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).ToList();

        return [];
    }

    private static string BuildDefaultOutputDir(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            var dir = Path.GetDirectoryName(inputPath) ?? ".";
            return Path.Combine(dir, "obfuscated");
        }
        return inputPath + ".obfuscated";
    }
}
