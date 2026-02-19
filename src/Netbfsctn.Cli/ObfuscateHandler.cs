using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL;
using Netbfsctn.Source;

namespace Netbfsctn.Cli;

public static class ObfuscateHandler
{
    public static int Execute(ObfuscationOptions options)
    {
        var logger = new ObfuscationLogger(options.Verbose, options.Quiet);

        if (!File.Exists(options.InputPath) && !Directory.Exists(options.InputPath))
        {
            logger.Error($"入力パスが見つかりません: {options.InputPath}");
            return 1;
        }

        var context = new ObfuscationContext
        {
            Options = options,
            Logger = logger
        };

        var mode = options.ResolvedMode;
        logger.Info($"モード: {mode}");
        logger.Info($"入力: {options.InputPath}");

        IObfuscationPipeline pipeline = mode switch
        {
            ObfuscationMode.IL => new ILObfuscationPipeline(),
            ObfuscationMode.Source => new SourceObfuscationPipeline(),
            _ => throw new InvalidOperationException($"未対応のモード: {mode}")
        };

        var result = pipeline.Execute(context);

        if (result.Success)
        {
            logger.Success($"難読化完了: {result.OutputPath}");
            logger.Info($"  名前変更: {result.RenamedSymbols} シンボル");
            logger.Info($"  文字列暗号化: {result.EncryptedStrings} 文字列");
            logger.Info($"  制御フロー難読化: {result.ObfuscatedMethods} メソッド");
            logger.Info($"  デッドコード挿入: {result.InsertedDeadCodeBlocks} ブロック");
            if (result.AntiIldasmApplied)
                logger.Info("  Anti-ILDASM: 適用済み");
            if (result.AntiDebugApplied)
                logger.Info("  Anti-Debug: 適用済み");
            if (result.AntiTamperingApplied)
                logger.Info("  Anti-Tampering: 適用済み");
            if (result.EncryptedMethodBodies > 0)
                logger.Info($"  NecroBit: {result.EncryptedMethodBodies} メソッド暗号化");
            if (result.HiddenMethodCalls > 0)
                logger.Info($"  呼び出し隠蔽: {result.HiddenMethodCalls} 呼び出し");
            if (result.MappingFilePath != null)
                logger.Info($"  マッピングファイル: {result.MappingFilePath}");
            if (result.ProtectedResources > 0)
                logger.Info($"  リソース保護: {result.ProtectedResources} リソース");
            if (result.VirtualizedMethods > 0)
                logger.Info($"  仮想化: {result.VirtualizedMethods} メソッド");
            return 0;
        }

        logger.Error(result.ErrorMessage ?? "不明なエラー");
        return 1;
    }
}
