namespace Netbfsctn.Core.Pipeline;

public class ObfuscationResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public int RenamedSymbols { get; set; }
    public int EncryptedStrings { get; set; }
    public int ObfuscatedMethods { get; set; }
    public int InsertedDeadCodeBlocks { get; set; }
}
