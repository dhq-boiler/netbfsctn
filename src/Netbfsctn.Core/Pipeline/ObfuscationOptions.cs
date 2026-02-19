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
    public bool EnableStringEncryption { get; init; } = true;
    public bool EnableControlFlow { get; init; } = true;
    public bool EnableDeadCode { get; init; } = true;
    public EncryptionMethod Encryption { get; init; } = EncryptionMethod.Xor;
    public bool Verbose { get; init; }
    public bool Quiet { get; init; }

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
