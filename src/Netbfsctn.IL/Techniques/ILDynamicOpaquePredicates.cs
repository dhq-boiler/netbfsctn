using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

/// <summary>
/// 動的不透明述語 (Dynamic Opaque Predicates) 難読化。
/// ヘルパー型に静的初期化されたバイトテーブル T と読み出しメソッド K(int) を注入し、
/// `K(IDX) != EXPECTED` という常に false な述語をブランチポイントに散布する。
/// 既存の invariant opaque predicate (例: `(x * 0) != 0`) と異なりメソッド呼び出しを介すため、
/// テンプレートマッチング型の ML 検出器を回避する。
/// </summary>
public class ILDynamicOpaquePredicates : IObfuscationTechnique<ModuleDef>
{
    public string Name => "動的不透明述語 (IL)";

    private const int TableSize = 64;
    private const double InsertProbability = 0.3;

    private Random _random = new(42);
    private byte[] _table = [];
    private TypeDef? _helperType;
    private MethodDef? _accessorMethod;

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        _random = new Random(module.Name.GetHashCode() ^ 0x0DAC);
        _table = new byte[TableSize];
        _random.NextBytes(_table);

        // ヘルパー型 (テーブル + アクセサ) を注入
        (_helperType, _accessorMethod) = InjectHelper(module);

        var inserted = 0;

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>" || type == _helperType) continue;
            if (IsInjectedHelperType(type.Name)) continue;

            foreach (var method in type.Methods.ToList())
            {
                if (!method.HasBody) continue;
                if (method.Body.Instructions.Count < 2) continue;
                if (method == _accessorMethod) continue;
                if (method.IsConstructor && method.IsStatic) continue; // cctor は触らない

                if (_random.NextDouble() > InsertProbability) continue;

                if (InsertPredicate(method, context))
                {
                    inserted++;
                    method.Body.KeepOldMaxStack = true;
                }
            }
        }

        result.DynamicOpaquePredicates += inserted;
        context.Logger.Verbose($"動的不透明述語: {inserted} 箇所挿入");
    }

    private (TypeDef helperType, MethodDef accessor) InjectHelper(ModuleDef module)
    {
        var helperType = new TypeDefUser("", "‌​‍‌", module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
                              | TypeAttributes.BeforeFieldInit;

        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);
        var tableField = new FieldDefUser("T",
            new FieldSig(byteArraySig),
            FieldAttributes.Private | FieldAttributes.Static);
        helperType.Fields.Add(tableField);

        // .cctor: T を初期化
        var cctor = new MethodDefUser(".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Private | MethodAttributes.Static
            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        BuildCctorBody(cctor, tableField, module);
        helperType.Methods.Add(cctor);

        // K(int idx) -> byte
        var accessor = new MethodDefUser("K",
            MethodSig.CreateStatic(module.CorLibTypes.Byte, module.CorLibTypes.Int32),
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig);
        BuildAccessorBody(accessor, tableField);
        helperType.Methods.Add(accessor);

        module.Types.Add(helperType);
        return (helperType, accessor);
    }

    /// <summary>
    /// cctor の IL: T = new byte[N]; T[0]=v0; T[1]=v1; ...
    /// </summary>
    private void BuildCctorBody(MethodDefUser cctor, FieldDef tableField, ModuleDef module)
    {
        var body = new CilBody();
        cctor.Body = body;
        body.KeepOldMaxStack = true;

        // new byte[TableSize]
        body.Instructions.Add(Instruction.CreateLdcI4(TableSize));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Stsfld, tableField));

        // T[i] = _table[i] for each i
        for (var i = 0; i < TableSize; i++)
        {
            body.Instructions.Add(new Instruction(OpCodes.Ldsfld, tableField));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.CreateLdcI4(_table[i]));
            body.Instructions.Add(new Instruction(OpCodes.Stelem_I1));
        }

        body.Instructions.Add(new Instruction(OpCodes.Ret));
    }

    /// <summary>
    /// K(int idx) -> byte の IL: return T[idx];
    /// </summary>
    private static void BuildAccessorBody(MethodDefUser accessor, FieldDef tableField)
    {
        var body = new CilBody();
        accessor.Body = body;
        body.KeepOldMaxStack = true;

        body.Instructions.Add(new Instruction(OpCodes.Ldsfld, tableField));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_U1));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
    }

    /// <summary>
    /// メソッドの先頭に動的不透明述語ベースのデッドコードを挿入する。
    /// パターン: if (K(IDX) != EXPECTED_VAL) goto continueLabel; <dead>; continueLabel: <original>
    /// K(IDX) は常に EXPECTED_VAL を返すため goto は常に taken。dead は実行されない。
    /// </summary>
    private bool InsertPredicate(MethodDef method, ObfuscationContext context)
    {
        var body = method.Body;
        var firstInstr = body.Instructions[0];
        var idx = body.Instructions.IndexOf(firstInstr);

        var tableIdx = _random.Next(TableSize);
        var expected = _table[tableIdx];

        // 命令列を生成
        var continueLabel = firstInstr; // 元の最初の命令へジャンプ
        var deadCode = GenerateDeadCode();

        var prologue = new List<Instruction>
        {
            // K(IDX)
            Instruction.CreateLdcI4(tableIdx),
            new Instruction(OpCodes.Call, _accessorMethod!),
            // EXPECTED
            Instruction.CreateLdcI4(expected),
            // K(IDX) != EXPECTED → 常に false なので Bne_Un は常に continueLabel に飛ぶ
            new Instruction(OpCodes.Bne_Un, continueLabel)
        };

        // 挿入順: prologue ... dead ... (元のメソッド本体)
        var insertList = new List<Instruction>(prologue);
        insertList.AddRange(deadCode);

        for (var i = 0; i < insertList.Count; i++)
            body.Instructions.Insert(idx + i, insertList[i]);

        return true;
    }

    private Instruction[] GenerateDeadCode()
    {
        // スタックバランスを保つ無害な算術
        return _random.Next(3) switch
        {
            0 => new[]
            {
                Instruction.CreateLdcI4(_random.Next(1, 1000)),
                Instruction.CreateLdcI4(_random.Next(1, 1000)),
                new Instruction(OpCodes.Add),
                new Instruction(OpCodes.Pop)
            },
            1 => new[]
            {
                Instruction.CreateLdcI4(_random.Next(1, 0xFFFF)),
                Instruction.CreateLdcI4(_random.Next(1, 0xFFFF)),
                new Instruction(OpCodes.Xor),
                Instruction.CreateLdcI4(_random.Next(1, 0xFF)),
                new Instruction(OpCodes.Or),
                new Instruction(OpCodes.Pop)
            },
            _ => new[]
            {
                Instruction.CreateLdcI4(_random.Next(1, 100)),
                Instruction.CreateLdcI4(_random.Next(1, 100)),
                new Instruction(OpCodes.Mul),
                Instruction.CreateLdcI4(_random.Next(1, 100)),
                new Instruction(OpCodes.Sub),
                new Instruction(OpCodes.Pop)
            }
        };
    }

    private static bool IsInjectedHelperType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => c is '​' or '‌' or '‍' or '﻿');
    }
}
