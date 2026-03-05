namespace Netbfsctn.Core.Pipeline;

public enum ObfuscationMode
{
    IL,
    Source
}

public enum EncryptionMethod
{
    Xor,
    Aes
}

public class ObfuscationOptions
{
    public required string InputPath { get; init; }
    public string? OutputPath { get; init; }
    public ObfuscationMode? Mode { get; init; }
    public bool EnableRename { get; init; } = true;
    public bool EnableRenameTypes { get; init; } = true;
    public bool EnableRenameFields { get; init; } = true;
    public bool EnableRenameMethods { get; init; } = true;
    public bool EnableRenameProperties { get; init; } = true;
    public bool EnableStringEncryption { get; init; } = true;
    public bool EnableControlFlow { get; init; } = true;
    public bool EnableDeadCode { get; init; } = true;
    public EncryptionMethod Encryption { get; init; } = EncryptionMethod.Xor;
    public bool Verbose { get; init; }
    public bool Quiet { get; init; }

    // 追加難読化テクニック (オプトイン)
    public bool EnableAntiIldasm { get; init; }
    public bool EnableAntiDebug { get; init; }
    public bool EnableAntiTampering { get; init; }
    public bool EnableNecroBit { get; init; }
    public bool EnableHideMethodCalls { get; init; }
    public bool EnableMappingFile { get; init; }
    public string? MappingFilePath { get; init; }
    public bool EnableResourceProtection { get; init; }
    public bool EnableCodeVirtualization { get; init; }

    // 複数アセンブリ同時難読化
    public string[] AdditionalInputPaths { get; init; } = [];
    public string[] AdditionalOutputPaths { get; init; } = [];

    public ObfuscationMode ResolvedMode =>
        Mode ?? InferMode(InputPath);

    private static ObfuscationMode InferMode(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".dll" or ".exe" => ObfuscationMode.IL,
            ".cs" => ObfuscationMode.Source,
            _ => Directory.Exists(path) ? ObfuscationMode.Source : ObfuscationMode.IL
        };
    }
}
