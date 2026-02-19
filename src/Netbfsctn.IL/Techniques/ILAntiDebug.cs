using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILAntiDebug : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "Anti-Debug (IL)";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        var isAttachedRef = module.ImportReference(
            typeof(System.Diagnostics.Debugger).GetProperty("IsAttached")!.GetGetMethod()!);
        var exitRef = module.ImportReference(
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
                    InjectDebugCheck(method, module, isAttachedRef, exitRef);
                }
            }
        }

        result.AntiDebugApplied = true;
        context.Logger.Verbose("デバッガ検出コードを注入しました");
    }

    private static void InjectIntoModuleCctor(
        ModuleDefinition module,
        MethodReference isAttachedRef,
        MethodReference exitRef)
    {
        var moduleType = module.Types.First(t => t.Name == "<Module>");

        var cctor = moduleType.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (cctor == null)
        {
            cctor = new MethodDefinition(
                ".cctor",
                MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.ImportReference(typeof(void)));
            cctor.Body.GetILProcessor().Append(
                cctor.Body.GetILProcessor().Create(OpCodes.Ret));
            moduleType.Methods.Add(cctor);
        }

        InjectDebugCheck(cctor, module, isAttachedRef, exitRef);
    }

    private static void InjectDebugCheck(
        MethodDefinition method,
        ModuleDefinition module,
        MethodReference isAttachedRef,
        MethodReference exitRef)
    {
        var il = method.Body.GetILProcessor();
        var firstInstruction = method.Body.Instructions[0];

        // if (Debugger.IsAttached) Environment.Exit(-1);
        var skipLabel = il.Create(OpCodes.Nop);

        il.InsertBefore(firstInstruction, il.Create(OpCodes.Call, isAttachedRef));
        il.InsertBefore(firstInstruction, il.Create(OpCodes.Brfalse, skipLabel));
        il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldc_I4_M1));
        il.InsertBefore(firstInstruction, il.Create(OpCodes.Call, exitRef));
        il.InsertBefore(firstInstruction, skipLabel);
    }
}
