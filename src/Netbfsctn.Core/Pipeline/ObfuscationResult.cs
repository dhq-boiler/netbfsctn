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

    // 追加テクニック結果
    public bool AntiIldasmApplied { get; set; }
    public bool AntiDebugApplied { get; set; }
    public bool AntiTamperingApplied { get; set; }
    public int EncryptedMethodBodies { get; set; }
    public int HiddenMethodCalls { get; set; }
    public string? MappingFilePath { get; set; }
    public int ProtectedResources { get; set; }
    public int VirtualizedMethods { get; set; }
}
