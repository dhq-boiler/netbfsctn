using Netbfsctn.Core.Logging;
using Netbfsctn.Core.NameGeneration;

namespace Netbfsctn.Core.Pipeline;

public class ObfuscationContext
{
    public required ObfuscationOptions Options { get; init; }
    public required ObfuscationLogger Logger { get; init; }
    public ConfusableNameGenerator NameGenerator { get; } = new();
    public Dictionary<string, string> NameMap { get; } = new();
}
