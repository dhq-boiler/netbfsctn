using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILDeadCodeInserter : IObfuscationTechnique<ModuleDef>
{
    public string Name => "デッドコード挿入 (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        // ダミーメソッドを追加
        AddDummyMethods(module, context, result);

        // 各メソッドに不透明述語付きデッドコードを挿入
        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;
            // 他のテクニックが注入したヘルパー型はスキップ
            if (IsInjectedHelperType(type.Name))
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (method.Body.Instructions.Count < 2) continue;
                if (method.Body.ExceptionHandlers.Count > 0) continue;

                InsertDeadCode(method, module, context, result);
            }
        }
    }

    private void InsertDeadCode(
        MethodDef method, ModuleDef module,
        ObfuscationContext context, ObfuscationResult result)
    {
        var body = method.Body;
        var first = body.Instructions[0];

        var afterDeadCode = new Instruction(OpCodes.Nop);

        // 条件: ldc.i4 int.MaxValue → ldc.i4.0 → mul → ldc.i4.0 → beq afterDeadCode
        var deadCodeStart = Instruction.CreateLdcI4(int.MaxValue);

        var insertIdx = body.Instructions.IndexOf(first);
        body.Instructions.Insert(insertIdx, deadCodeStart);
        body.Instructions.Insert(insertIdx + 1, new Instruction(OpCodes.Ldc_I4_0));
        body.Instructions.Insert(insertIdx + 2, new Instruction(OpCodes.Mul));
        body.Instructions.Insert(insertIdx + 3, new Instruction(OpCodes.Ldc_I4_0));
        body.Instructions.Insert(insertIdx + 4, new Instruction(OpCodes.Beq, afterDeadCode));

        // デッドコード: 到達不能な命令群
        body.Instructions.Insert(insertIdx + 5, Instruction.CreateLdcI4(42));
        body.Instructions.Insert(insertIdx + 6, new Instruction(OpCodes.Pop));
        body.Instructions.Insert(insertIdx + 7, Instruction.CreateLdcI4(99));
        body.Instructions.Insert(insertIdx + 8, new Instruction(OpCodes.Pop));

        body.Instructions.Insert(insertIdx + 9, afterDeadCode);

        result.InsertedDeadCodeBlocks++;
        context.Logger.Verbose($"デッドコード挿入: {method.Name}");
    }

    private void AddDummyMethods(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var random = new Random(42);

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>")
                continue;
            if (type.IsInterface)
                continue;
            if (type.IsEnum)
                continue;
            if (type.IsValueType)
                continue;

            var dummyCount = random.Next(1, 3);
            for (var i = 0; i < dummyCount; i++)
            {
                var dummyName = context.NameGenerator.Next();
                var dummyMethod = new MethodDefUser(
                    dummyName,
                    MethodSig.CreateStatic(module.CorLibTypes.Int32),
                    dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                    dnlib.DotNet.MethodAttributes.Private | dnlib.DotNet.MethodAttributes.Static
                        | dnlib.DotNet.MethodAttributes.HideBySig);

                var body = new CilBody();
                dummyMethod.Body = body;
                body.Instructions.Add(Instruction.CreateLdcI4(random.Next(1000)));
                body.Instructions.Add(Instruction.CreateLdcI4(random.Next(1000)));
                body.Instructions.Add(new Instruction(OpCodes.Add));
                body.Instructions.Add(new Instruction(OpCodes.Ret));

                type.Methods.Add(dummyMethod);
                result.InsertedDeadCodeBlocks++;
                context.Logger.Verbose($"ダミーメソッド追加: {dummyName} in {type.Name}");
            }
        }
    }

    private static bool IsInjectedHelperType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF');
    }
}
