using System.CommandLine;
using Netbfsctn.Cli;
using Netbfsctn.Core.Pipeline;

var inputArgument = new Argument<string>("input")
{
    Description = "入力パス (DLL/EXE or .cs ファイル/ディレクトリ)"
};

var outputOption = new Option<string?>("--output", "-o")
{
    Description = "出力先パス"
};

var modeOption = new Option<string?>("--mode", "-m")
{
    Description = "動作モード (il|source)"
};

var noRenameOption = new Option<bool>("--no-rename")
{
    Description = "名前難読化を無効化"
};

var noStringsOption = new Option<bool>("--no-strings")
{
    Description = "文字列暗号化を無効化"
};

var noControlFlowOption = new Option<bool>("--no-control-flow")
{
    Description = "制御フロー難読化を無効化"
};

var noDeadCodeOption = new Option<bool>("--no-dead-code")
{
    Description = "デッドコード挿入を無効化"
};

var encryptionOption = new Option<string>("--encryption")
{
    Description = "暗号化方式 (xor|aes)",
    DefaultValueFactory = _ => "xor"
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "詳細出力"
};

var quietOption = new Option<bool>("--quiet", "-q")
{
    Description = "最小限の出力"
};

var antiIldasmOption = new Option<bool>("--anti-ildasm")
{
    Description = "Anti-ILDASM 属性を付与"
};

var antiDebugOption = new Option<bool>("--anti-debug")
{
    Description = "デバッガ検出コードを注入"
};

var antiTamperOption = new Option<bool>("--anti-tamper")
{
    Description = "改ざん検出コードを注入"
};

var necroBitOption = new Option<bool>("--necrobit")
{
    Description = "メソッドボディを暗号化"
};

var hideCallsOption = new Option<bool>("--hide-calls")
{
    Description = "メソッド呼び出しをリフレクション経由に置換"
};

var mappingFileOption = new Option<string?>("--mapping-file")
{
    Description = "名前マッピングファイルを出力 (パス指定可)"
};

var protectResourcesOption = new Option<bool>("--protect-resources")
{
    Description = "埋め込みリソースを暗号化"
};

var virtualizeOption = new Option<bool>("--virtualize")
{
    Description = "メソッドをカスタム VM バイトコードに変換"
};

var rootCommand = new RootCommand("netbfsctn - .NET 難読化 CLI ツール");
rootCommand.Arguments.Add(inputArgument);
rootCommand.Options.Add(outputOption);
rootCommand.Options.Add(modeOption);
rootCommand.Options.Add(noRenameOption);
rootCommand.Options.Add(noStringsOption);
rootCommand.Options.Add(noControlFlowOption);
rootCommand.Options.Add(noDeadCodeOption);
rootCommand.Options.Add(encryptionOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(quietOption);
rootCommand.Options.Add(antiIldasmOption);
rootCommand.Options.Add(antiDebugOption);
rootCommand.Options.Add(antiTamperOption);
rootCommand.Options.Add(necroBitOption);
rootCommand.Options.Add(hideCallsOption);
rootCommand.Options.Add(mappingFileOption);
rootCommand.Options.Add(protectResourcesOption);
rootCommand.Options.Add(virtualizeOption);

rootCommand.SetAction(parseResult =>
{
    var input = parseResult.GetValue(inputArgument);
    var output = parseResult.GetValue(outputOption);
    var mode = parseResult.GetValue(modeOption);
    var noRename = parseResult.GetValue(noRenameOption);
    var noStrings = parseResult.GetValue(noStringsOption);
    var noControlFlow = parseResult.GetValue(noControlFlowOption);
    var noDeadCode = parseResult.GetValue(noDeadCodeOption);
    var encryption = parseResult.GetValue(encryptionOption);
    var verbose = parseResult.GetValue(verboseOption);
    var quiet = parseResult.GetValue(quietOption);
    var antiIldasm = parseResult.GetValue(antiIldasmOption);
    var antiDebug = parseResult.GetValue(antiDebugOption);
    var antiTamper = parseResult.GetValue(antiTamperOption);
    var necroBit = parseResult.GetValue(necroBitOption);
    var hideCalls = parseResult.GetValue(hideCallsOption);
    var mappingFile = parseResult.GetValue(mappingFileOption);
    var protectResources = parseResult.GetValue(protectResourcesOption);
    var virtualize = parseResult.GetValue(virtualizeOption);

    var options = new ObfuscationOptions
    {
        InputPath = input!,
        OutputPath = output,
        Mode = mode?.ToLowerInvariant() switch
        {
            "il" => ObfuscationMode.IL,
            "source" => ObfuscationMode.Source,
            _ => null
        },
        EnableRename = !noRename,
        EnableStringEncryption = !noStrings,
        EnableControlFlow = !noControlFlow,
        EnableDeadCode = !noDeadCode,
        Encryption = encryption?.ToLowerInvariant() == "aes"
            ? EncryptionMethod.Aes
            : EncryptionMethod.Xor,
        Verbose = verbose,
        Quiet = quiet,
        EnableAntiIldasm = antiIldasm,
        EnableAntiDebug = antiDebug,
        EnableAntiTampering = antiTamper,
        EnableNecroBit = necroBit,
        EnableHideMethodCalls = hideCalls,
        EnableMappingFile = mappingFile != null,
        MappingFilePath = mappingFile,
        EnableResourceProtection = protectResources,
        EnableCodeVirtualization = virtualize
    };

    return ObfuscateHandler.Execute(options);
});

return rootCommand.Parse(args).Invoke();
