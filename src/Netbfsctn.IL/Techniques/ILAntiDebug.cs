using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILAntiDebug : IObfuscationTechnique<ModuleDef>
{
    public string Name => "Anti-Debug (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);
        var isAttachedRef = importer.Import(
            typeof(System.Diagnostics.Debugger).GetProperty("IsAttached")!.GetGetMethod()!);
        var exitRef = importer.Import(
            typeof(Environment).GetMethod("Exit", [typeof(int)])!);

        // モジュール初期化子にデバッガ検出コードを注入
        InjectIntoModuleCctor(module, isAttachedRef, exitRef);

        // 各メソッド先頭にもチェックを分散挿入
        var random = new Random(module.Name.GetHashCode());
        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (method.IsConstructor) continue;
                if (method.Body.Instructions.Count < 2) continue;

                // 約30%のメソッドにチェックを分散挿入
                if (random.Next(100) < 30)
                {
                    InjectDebugCheck(method, isAttachedRef, exitRef);
                }
            }
        }

        result.AntiDebugApplied = true;
        context.Logger.Verbose("デバッガ検出コードを注入しました");
    }

    private static void InjectIntoModuleCctor(
        ModuleDef module,
        IMethod isAttachedRef,
        IMethod exitRef)
    {
        var moduleType = module.GlobalType;

        var cctor = moduleType.FindStaticConstructor();
        if (cctor == null)
        {
            cctor = new MethodDefUser(
                ".cctor",
                MethodSig.CreateStatic(module.CorLibTypes.Void),
                dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                dnlib.DotNet.MethodAttributes.Static | dnlib.DotNet.MethodAttributes.Private
                    | dnlib.DotNet.MethodAttributes.HideBySig
                    | dnlib.DotNet.MethodAttributes.SpecialName | dnlib.DotNet.MethodAttributes.RTSpecialName);
            cctor.Body = new CilBody();
            cctor.Body.Instructions.Add(new Instruction(OpCodes.Ret));
            moduleType.Methods.Add(cctor);
        }

        InjectDebugCheck(cctor, isAttachedRef, exitRef);
    }

    private static void InjectDebugCheck(
        MethodDef method,
        IMethod isAttachedRef,
        IMethod exitRef)
    {
        var body = method.Body;
        var firstInstruction = body.Instructions[0];
        var idx = 0;

        // if (Debugger.IsAttached) Environment.Exit(-1);
        var skipLabel = new Instruction(OpCodes.Nop);

        body.Instructions.Insert(idx++, new Instruction(OpCodes.Call, isAttachedRef));
        body.Instructions.Insert(idx++, new Instruction(OpCodes.Brfalse, skipLabel));
        body.Instructions.Insert(idx++, new Instruction(OpCodes.Ldc_I4_M1));
        body.Instructions.Insert(idx++, new Instruction(OpCodes.Call, exitRef));
        body.Instructions.Insert(idx, skipLabel);
    }
}
