namespace Netbfsctn.Core.Pipeline;

public interface IObfuscationPipeline
{
    ObfuscationResult Execute(ObfuscationContext context);
}
