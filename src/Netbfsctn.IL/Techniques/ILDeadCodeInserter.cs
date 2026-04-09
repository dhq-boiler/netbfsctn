using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILDeadCodeInserter : IObfuscationTechnique<ModuleDef>
{
    public string Name => "デッドコード挿入 (IL)";

    private Random _random = new(42);

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        _random = new Random(module.Name.GetHashCode());

        AddDummyMethods(module, context, result);

        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;
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
        var insertIdx = body.Instructions.IndexOf(first);

        // 不透明述語をランダムに選択
        var predicateInstructions = GenerateOpaquePredicate(afterDeadCode);

        for (var i = 0; i < predicateInstructions.Length; i++)
            body.Instructions.Insert(insertIdx + i, predicateInstructions[i]);

        // デッドコード本体をランダムに生成
        var deadInstructions = GenerateDeadCodeBody(module);
        var deadStart = insertIdx + predicateInstructions.Length;
        for (var i = 0; i < deadInstructions.Length; i++)
            body.Instructions.Insert(deadStart + i, deadInstructions[i]);

        body.Instructions.Insert(deadStart + deadInstructions.Length, afterDeadCode);

        result.InsertedDeadCodeBlocks++;
        context.Logger.Verbose($"デッドコード挿入: {method.Name}");
    }

    /// <summary>
    /// 常に false となる不透明述語を多パターンで生成する。
    /// 全パターンでスタックは 0 → 0 でバランスし、条件分岐で afterDeadCode へジャンプする。
    /// </summary>
    private Instruction[] GenerateOpaquePredicate(Instruction afterDeadCode)
    {
        return (_random.Next(8)) switch
        {
            // (a * 0) != 0 → always false (beq jumps)
            0 => [
                Instruction.CreateLdcI4(_random.Next(1, 10000)),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Mul),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Beq, afterDeadCode)
            ],
            // (a - a) != 0 → always false
            1 => GenerateSubSelfPredicate(afterDeadCode),
            // (a ^ a) != 0 → always false
            2 => GenerateXorSelfPredicate(afterDeadCode),
            // (a & 0) != 0 → always false
            3 => [
                Instruction.CreateLdcI4(_random.Next(1, 10000)),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.And),
                new(OpCodes.Brfalse, afterDeadCode)
            ],
            // (0 / a) != 0 → always false (a != 0)
            4 => [
                new(OpCodes.Ldc_I4_0),
                Instruction.CreateLdcI4(_random.Next(1, 10000)),
                new(OpCodes.Div),
                new(OpCodes.Brfalse, afterDeadCode)
            ],
            // (a % 1) != 0 → always false
            5 => [
                Instruction.CreateLdcI4(_random.Next(1, 10000)),
                new(OpCodes.Ldc_I4_1),
                new(OpCodes.Rem),
                new(OpCodes.Brfalse, afterDeadCode)
            ],
            // (a * b - a * b) != 0 → always false
            6 => GenerateMulSubPredicate(afterDeadCode),
            // ((a | b) & 0) != 0 → always false
            _ => [
                Instruction.CreateLdcI4(_random.Next(1, 10000)),
                Instruction.CreateLdcI4(_random.Next(1, 10000)),
                new(OpCodes.Or),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.And),
                new(OpCodes.Brfalse, afterDeadCode)
            ]
        };
    }

    // (a - a) == 0 → beq jumps over dead code
    private Instruction[] GenerateSubSelfPredicate(Instruction afterDeadCode)
    {
        var val = _random.Next(1, 10000);
        return [
            Instruction.CreateLdcI4(val),
            Instruction.CreateLdcI4(val),
            new(OpCodes.Sub),
            new(OpCodes.Brfalse, afterDeadCode)
        ];
    }

    // (a ^ a) == 0 → brfalse jumps
    private Instruction[] GenerateXorSelfPredicate(Instruction afterDeadCode)
    {
        var val = _random.Next(1, 10000);
        return [
            Instruction.CreateLdcI4(val),
            Instruction.CreateLdcI4(val),
            new(OpCodes.Xor),
            new(OpCodes.Brfalse, afterDeadCode)
        ];
    }

    // (a * b) - (a * b) == 0
    private Instruction[] GenerateMulSubPredicate(Instruction afterDeadCode)
    {
        var a = _random.Next(1, 100);
        var b = _random.Next(1, 100);
        return [
            Instruction.CreateLdcI4(a),
            Instruction.CreateLdcI4(b),
            new(OpCodes.Mul),
            Instruction.CreateLdcI4(a),
            Instruction.CreateLdcI4(b),
            new(OpCodes.Mul),
            new(OpCodes.Sub),
            new(OpCodes.Brfalse, afterDeadCode)
        ];
    }

    /// <summary>
    /// 到達不能なデッドコード本体を多パターンで生成する。
    /// スタックをバランスさせつつ、リアルなコードに見えるようにする。
    /// </summary>
    private Instruction[] GenerateDeadCodeBody(ModuleDef module)
    {
        return (_random.Next(6)) switch
        {
            // 算術演算チェーン
            0 => GenerateArithmeticChain(),
            // ローカル変数風の操作
            1 => GenerateLocalVarOps(),
            // 比較とポップ
            2 => GenerateCompareOps(),
            // ビット操作チェーン
            3 => GenerateBitwiseChain(),
            // ネストした算術
            4 => GenerateNestedArithmetic(),
            // 最小限
            _ => [
                Instruction.CreateLdcI4(_random.Next(1000)),
                new(OpCodes.Pop)
            ]
        };
    }

    private Instruction[] GenerateArithmeticChain()
    {
        var ops = new[] { OpCodes.Add, OpCodes.Sub, OpCodes.Mul };
        var instructions = new List<Instruction>();
        instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 500)));
        var chainLen = 2 + _random.Next(3);
        for (var i = 0; i < chainLen; i++)
        {
            instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 500)));
            instructions.Add(new Instruction(ops[_random.Next(ops.Length)]));
        }
        instructions.Add(new Instruction(OpCodes.Pop));
        return instructions.ToArray();
    }

    private Instruction[] GenerateLocalVarOps()
    {
        var a = _random.Next(1, 1000);
        var b = _random.Next(1, 1000);
        return [
            Instruction.CreateLdcI4(a),
            Instruction.CreateLdcI4(b),
            new(OpCodes.Add),
            Instruction.CreateLdcI4(a + b),
            new(OpCodes.Sub),
            new(OpCodes.Pop)
        ];
    }

    private Instruction[] GenerateCompareOps()
    {
        return [
            Instruction.CreateLdcI4(_random.Next(1, 1000)),
            Instruction.CreateLdcI4(_random.Next(1, 1000)),
            new(OpCodes.Cgt),
            new(OpCodes.Pop)
        ];
    }

    private Instruction[] GenerateBitwiseChain()
    {
        var ops = new[] { OpCodes.And, OpCodes.Or, OpCodes.Xor };
        var instructions = new List<Instruction>();
        instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 0xFFFF)));
        var chainLen = 2 + _random.Next(2);
        for (var i = 0; i < chainLen; i++)
        {
            instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 0xFFFF)));
            instructions.Add(new Instruction(ops[_random.Next(ops.Length)]));
        }
        instructions.Add(new Instruction(OpCodes.Pop));
        return instructions.ToArray();
    }

    private Instruction[] GenerateNestedArithmetic()
    {
        // (a + b) * (c - d) → pop
        return [
            Instruction.CreateLdcI4(_random.Next(1, 100)),
            Instruction.CreateLdcI4(_random.Next(1, 100)),
            new(OpCodes.Add),
            Instruction.CreateLdcI4(_random.Next(1, 100)),
            Instruction.CreateLdcI4(_random.Next(1, 100)),
            new(OpCodes.Sub),
            new(OpCodes.Mul),
            new(OpCodes.Pop)
        ];
    }

    private void AddDummyMethods(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>") continue;
            if (type.IsInterface) continue;
            if (type.IsEnum) continue;
            if (type.IsValueType) continue;

            var dummyCount = _random.Next(1, 3);
            for (var i = 0; i < dummyCount; i++)
            {
                var dummyMethod = GenerateDummyMethod(module, context);
                type.Methods.Add(dummyMethod);
                result.InsertedDeadCodeBlocks++;
                context.Logger.Verbose($"ダミーメソッド追加: {dummyMethod.Name} in {type.Name}");
            }
        }
    }

    private MethodDefUser GenerateDummyMethod(ModuleDef module, ObfuscationContext context)
    {
        var dummyName = context.NameGenerator.Next();
        var returnType = (_random.Next(3)) switch
        {
            0 => module.CorLibTypes.Int32,
            1 => module.CorLibTypes.Boolean,
            _ => module.CorLibTypes.Int64
        };

        var dummyMethod = new MethodDefUser(
            dummyName,
            MethodSig.CreateStatic(returnType),
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig);

        dummyMethod.Body = (_random.Next(5)) switch
        {
            0 => GenerateDummyArithmeticBody(module),
            1 => GenerateDummyBitwiseBody(module),
            2 => GenerateDummyComparisonBody(module),
            3 => GenerateDummyConditionalBody(module),
            _ => GenerateDummyMultiStepBody(module)
        };

        return dummyMethod;
    }

    private CilBody GenerateDummyArithmeticBody(ModuleDef module)
    {
        var body = new CilBody();
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 1000)));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 1000)));
        var ops = new[] { OpCodes.Add, OpCodes.Sub, OpCodes.Mul };
        body.Instructions.Add(new Instruction(ops[_random.Next(ops.Length)]));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        return body;
    }

    private CilBody GenerateDummyBitwiseBody(ModuleDef module)
    {
        var body = new CilBody();
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 0xFFFF)));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 0xFFFF)));
        var ops = new[] { OpCodes.And, OpCodes.Or, OpCodes.Xor };
        body.Instructions.Add(new Instruction(ops[_random.Next(ops.Length)]));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 0xFF)));
        body.Instructions.Add(new Instruction(OpCodes.Shr));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        return body;
    }

    private CilBody GenerateDummyComparisonBody(ModuleDef module)
    {
        var body = new CilBody();
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 1000)));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 1000)));
        var ops = new[] { OpCodes.Cgt, OpCodes.Clt, OpCodes.Ceq };
        body.Instructions.Add(new Instruction(ops[_random.Next(ops.Length)]));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        return body;
    }

    private CilBody GenerateDummyConditionalBody(ModuleDef module)
    {
        var body = new CilBody();
        var retTrue = Instruction.CreateLdcI4(1);
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 1000)));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 1000)));
        body.Instructions.Add(new Instruction(OpCodes.Bgt, retTrue));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        body.Instructions.Add(retTrue);
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        return body;
    }

    private CilBody GenerateDummyMultiStepBody(ModuleDef module)
    {
        var body = new CilBody();
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 100)));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 100)));
        body.Instructions.Add(new Instruction(OpCodes.Add));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 100)));
        body.Instructions.Add(new Instruction(OpCodes.Mul));
        body.Instructions.Add(Instruction.CreateLdcI4(_random.Next(1, 100)));
        body.Instructions.Add(new Instruction(OpCodes.Sub));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I8));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        return body;
    }

    private static bool IsInjectedHelperType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF');
    }
}
