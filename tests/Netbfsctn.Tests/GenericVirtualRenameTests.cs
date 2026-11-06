using dnlib.DotNet;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL.Techniques;

namespace Netbfsctn.Tests;

public abstract class GenericDragSample<T>
{
    protected abstract T GetDragData();
    protected abstract int CreateDragAdorner(T data);
    public int Move() => CreateDragAdorner(GetDragData());
}

public sealed class StringDragSample : GenericDragSample<string>
{
    protected override string GetDragData() => "drag";
    protected override int CreateDragAdorner(string data) => data.Length;
}

public sealed class ObjectDragSample : GenericDragSample<object>
{
    protected override object GetDragData() => new object();
    protected override int CreateDragAdorner(object data) => data.GetHashCode();
}

public class GenericVirtualRenameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericVirtualCalls_KeepReferencesToRenamedDefinitions(bool renamePublic)
    {
        using var module = ModuleDefMD.Load(typeof(GenericVirtualRenameTests).Assembly.Location);
        var baseType = module.GetTypes().Single(t => t.Name == "GenericDragSample`1");
        var calls = baseType.Methods.Single(m => m.Name == "Move").Body.Instructions
            .Select(i => i.Operand).OfType<MemberRef>()
            .Where(m => m.Name == "GetDragData" || m.Name == "CreateDragAdorner")
            .Select(reference => (Reference: reference, Definition: baseType.Methods.Single(m => m.Name == reference.Name)))
            .ToArray();
        Assert.Equal(2, calls.Length);
        var context = new ObfuscationContext
        {
            Options = new ObfuscationOptions { InputPath = "sample.dll", EnableRenamePublic = renamePublic },
            Logger = new ObfuscationLogger(verbose: false, quiet: true)
        };

        new ILNameObfuscator().Apply(module, context, new ObfuscationResult { Success = true });

        foreach (var call in calls)
            Assert.Equal(call.Definition.Name, call.Reference.Name);
    }
}
