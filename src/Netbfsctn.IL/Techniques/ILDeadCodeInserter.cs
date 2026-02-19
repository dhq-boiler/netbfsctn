using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILDeadCodeInserter : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "デッドコード挿入 (IL)";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        // ダミーメソッドを追加
        AddDummyMethods(module, context, result);

        // 各メソッドに不透明述語付きデッドコードを挿入
        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (method.Body.Instructions.Count < 2) continue;
                if (method.Body.HasExceptionHandlers) continue;

                InsertDeadCode(method, module, context, result);
            }
        }
    }

    private void InsertDeadCode(
        MethodDefinition method, ModuleDefinition module,
        ObfuscationContext context, ObfuscationResult result)
    {
        var body = method.Body;
        var il = body.GetILProcessor();
        var instructions = body.Instructions.ToList();

        // メソッド先頭にデッドコードブロックを挿入
        // 不透明述語: int.MaxValue * 0 != 0 → 常に false
        var first = body.Instructions[0];

        var afterDeadCode = il.Create(OpCodes.Nop);

        // 条件: ldc.i4 int.MaxValue → ldc.i4.0 → mul → ldc.i4.0 → beq afterDeadCode
        // つまり (int.MaxValue * 0) == 0 → true → 分岐して dead code をスキップ
        var deadCodeStart = il.Create(OpCodes.Ldc_I4, int.MaxValue);

        il.InsertBefore(first, deadCodeStart);
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Mul));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Beq, afterDeadCode));

        // デッドコード: 到達不能な命令群
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 42));
        il.InsertBefore(first, il.Create(OpCodes.Pop));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 99));
        il.InsertBefore(first, il.Create(OpCodes.Pop));

        il.InsertBefore(first, afterDeadCode);

        result.InsertedDeadCodeBlocks++;
        context.Logger.Verbose($"デッドコード挿入: {method.Name}");
    }

    private void AddDummyMethods(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        var random = new Random(42);

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>")
                continue;
            if (type.IsInterface)
                continue;

            var dummyCount = random.Next(1, 3);
            for (var i = 0; i < dummyCount; i++)
            {
                var dummyName = context.NameGenerator.Next();
                var dummyMethod = new MethodDefinition(
                    dummyName,
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                    module.ImportReference(typeof(int)));

                var il = dummyMethod.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldc_I4, random.Next(1000)));
                il.Append(il.Create(OpCodes.Ldc_I4, random.Next(1000)));
                il.Append(il.Create(OpCodes.Add));
                il.Append(il.Create(OpCodes.Ret));

                type.Methods.Add(dummyMethod);
                result.InsertedDeadCodeBlocks++;
                context.Logger.Verbose($"ダミーメソッド追加: {dummyName} in {type.Name}");
            }
        }
    }
}
