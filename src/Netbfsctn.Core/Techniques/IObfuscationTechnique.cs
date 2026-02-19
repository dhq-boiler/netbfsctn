using Netbfsctn.Core.Pipeline;

namespace Netbfsctn.Core.Techniques;

public interface IObfuscationTechnique<TModule>
{
    string Name { get; }
    void Apply(TModule module, ObfuscationContext context, ObfuscationResult result);
}
