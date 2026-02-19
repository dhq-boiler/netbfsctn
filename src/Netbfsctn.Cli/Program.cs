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
        Quiet = quiet
    };

    return ObfuscateHandler.Execute(options);
});

return rootCommand.Parse(args).Invoke();
