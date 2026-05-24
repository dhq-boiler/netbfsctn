using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

/// <summary>
/// Mixed Boolean-Arithmetic (MBA) 難読化。
/// add/sub/xor/and/or を Z/2^n 上の恒等式で意味等価な命令列に置換し、
/// 定数ロード (ldc.i4/i8) も同等な算術評価に展開する。
/// SMT ソルバによる自動簡約に対し計算量的非可逆となる。
/// </summary>
public class ILMixedBooleanArithmetic : IObfuscationTechnique<ModuleDef>
{
    public string Name => "MBA 難読化 (IL)";

    private Random _random = new(42);

    private const double ArithReplaceProbability = 0.5;
    private const double ConstUnfoldProbability = 0.3;

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        _random = new Random(module.Name.GetHashCode() ^ 0x1BA);

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

                ApplyToMethod(method, module, context, result);
            }
        }
    }

    private void ApplyToMethod(
        MethodDef method, ModuleDef module,
        ObfuscationContext context, ObfuscationResult result)
    {
        var body = method.Body;
        var corLib = module.CorLibTypes;

        // メソッド単位で一時ローカルを遅延確保
        Local? tmpI32A = null, tmpI32B = null;
        Local? tmpI64A = null, tmpI64B = null;

        Local GetTmp(bool isI64, bool slotB)
        {
            if (isI64)
            {
                if (slotB)
                {
                    tmpI64B ??= AddLocal(body, corLib.Int64);
                    return tmpI64B;
                }
                tmpI64A ??= AddLocal(body, corLib.Int64);
                return tmpI64A;
            }
            if (slotB)
            {
                tmpI32B ??= AddLocal(body, corLib.Int32);
                return tmpI32B;
            }
            tmpI32A ??= AddLocal(body, corLib.Int32);
            return tmpI32A;
        }

        // 元命令スナップショット (新規挿入された命令を再処理しないため)
        var originalInstrs = body.Instructions.ToList();
        var transformed = 0;

        foreach (var instr in originalInstrs)
        {
            // 二項算術命令の MBA 化
            if (IsTargetArith(instr.OpCode.Code))
            {
                if (_random.NextDouble() > ArithReplaceProbability) continue;

                var idx = body.Instructions.IndexOf(instr);
                if (idx < 2) continue;

                var prev1 = body.Instructions[idx - 1];
                var prev2 = body.Instructions[idx - 2];
                var operandType = InferBinaryOperandType(method, prev1, prev2);
                if (operandType == OperandIntType.Unknown) continue;

                var isI64 = operandType == OperandIntType.Int64;
                var tmpA = GetTmp(isI64, false);
                var tmpB = GetTmp(isI64, true);

                var replacement = GenerateMbaPattern(instr.OpCode.Code, isI64, tmpA, tmpB);
                if (replacement == null) continue;

                var originalName = instr.OpCode.Name;
                ReplaceWithSequence(body, idx, instr, replacement);
                transformed++;
                context.Logger.Verbose($"MBA算術変換: {method.Name} @ {originalName}");
            }
            // 定数 unfolding
            else if (IsTargetConstant(instr.OpCode.Code))
            {
                if (_random.NextDouble() > ConstUnfoldProbability) continue;

                var idx = body.Instructions.IndexOf(instr);
                var isI64 = instr.OpCode.Code == Code.Ldc_I8;
                var replacement = GenerateConstUnfold(instr, isI64);
                if (replacement == null) continue;

                var originalName = instr.OpCode.Name;
                ReplaceWithSequence(body, idx, instr, replacement);
                transformed++;
                context.Logger.Verbose($"MBA定数展開: {method.Name} @ {originalName}");
            }
        }

        if (transformed > 0)
        {
            body.KeepOldMaxStack = true;
            result.MbaTransformations += transformed;
        }
    }

    /// <summary>
    /// 元命令を nop に書き換え、その直後に置換命令列を挿入する。
    /// 元命令の Instruction オブジェクトは破棄せず、branch target を保持する。
    /// </summary>
    private static void ReplaceWithSequence(
        CilBody body, int idx, Instruction original, Instruction[] replacement)
    {
        original.OpCode = OpCodes.Nop;
        original.Operand = null;
        for (var i = 0; i < replacement.Length; i++)
            body.Instructions.Insert(idx + 1 + i, replacement[i]);
    }

    private static Local AddLocal(CilBody body, TypeSig sig)
    {
        var local = new Local(sig);
        body.Variables.Add(local);
        return local;
    }

    private static bool IsTargetArith(Code code) =>
        code is Code.Add or Code.Sub or Code.Xor or Code.And or Code.Or;

    private static bool IsTargetConstant(Code code) =>
        code is Code.Ldc_I4 or Code.Ldc_I4_S
              or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3
              or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7
              or Code.Ldc_I4_8 or Code.Ldc_I4_M1
              or Code.Ldc_I8;

    private enum OperandIntType { Unknown, Int32, Int64 }

    /// <summary>
    /// 算術命令直前の 2 命令から両オペランドの整数型を推論する。
    /// 両方とも Int32 (or Int64) と確定できる場合のみ MBA 変換対象とする。
    /// </summary>
    private static OperandIntType InferBinaryOperandType(
        MethodDef method, Instruction prev1, Instruction prev2)
    {
        var t1 = InferPushedType(method, prev1);
        var t2 = InferPushedType(method, prev2);
        if (t1 == OperandIntType.Unknown || t2 == OperandIntType.Unknown)
            return OperandIntType.Unknown;
        if (t1 != t2) return OperandIntType.Unknown;
        return t1;
    }

    private static OperandIntType InferPushedType(MethodDef method, Instruction instr)
    {
        switch (instr.OpCode.Code)
        {
            case Code.Ldc_I4:
            case Code.Ldc_I4_S:
            case Code.Ldc_I4_0:
            case Code.Ldc_I4_1:
            case Code.Ldc_I4_2:
            case Code.Ldc_I4_3:
            case Code.Ldc_I4_4:
            case Code.Ldc_I4_5:
            case Code.Ldc_I4_6:
            case Code.Ldc_I4_7:
            case Code.Ldc_I4_8:
            case Code.Ldc_I4_M1:
            case Code.Conv_I4:
            case Code.Conv_U4:
            case Code.Conv_Ovf_I4:
            case Code.Conv_Ovf_U4:
            case Code.Conv_Ovf_I4_Un:
            case Code.Conv_Ovf_U4_Un:
            case Code.Ldlen:
            case Code.Ldelem_I4:
            case Code.Ldelem_U4:
            case Code.Ldind_I4:
            case Code.Ldind_U4:
                return OperandIntType.Int32;

            case Code.Ldc_I8:
            case Code.Conv_I8:
            case Code.Conv_U8:
            case Code.Conv_Ovf_I8:
            case Code.Conv_Ovf_U8:
            case Code.Conv_Ovf_I8_Un:
            case Code.Conv_Ovf_U8_Un:
            case Code.Ldelem_I8:
            case Code.Ldind_I8:
                return OperandIntType.Int64;

            case Code.Ldloc:
            case Code.Ldloc_S:
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
            {
                var local = instr.GetLocal(method.Body.Variables);
                return ClassifyTypeSig(local?.Type);
            }

            case Code.Ldarg:
            case Code.Ldarg_S:
            case Code.Ldarg_0:
            case Code.Ldarg_1:
            case Code.Ldarg_2:
            case Code.Ldarg_3:
            {
                var param = instr.GetParameter(method.Parameters);
                return ClassifyTypeSig(param?.Type);
            }

            case Code.Ldfld:
            case Code.Ldsfld:
            {
                if (instr.Operand is IField field)
                    return ClassifyTypeSig(field.FieldSig?.Type);
                return OperandIntType.Unknown;
            }

            default:
                return OperandIntType.Unknown;
        }
    }

    private static OperandIntType ClassifyTypeSig(TypeSig? sig)
    {
        if (sig == null) return OperandIntType.Unknown;
        var et = sig.ElementType;
        return et switch
        {
            ElementType.I4 or ElementType.U4 => OperandIntType.Int32,
            ElementType.Boolean or ElementType.Char
              or ElementType.I1 or ElementType.U1
              or ElementType.I2 or ElementType.U2 => OperandIntType.Int32,
            ElementType.I8 or ElementType.U8 => OperandIntType.Int64,
            _ => OperandIntType.Unknown
        };
    }

    // MBA パターン生成

    /// <summary>
    /// スタックトップ 2 要素 (a, b) を tmpA, tmpB に退避し、指定演算の MBA 等価式を計算する命令列を返す。
    /// 入力 stack = [..., a, b]、出力 stack = [..., a OP b]
    /// </summary>
    private Instruction[]? GenerateMbaPattern(Code code, bool isI64, Local tmpA, Local tmpB)
    {
        var prologue = new List<Instruction>
        {
            new(OpCodes.Stloc, tmpB),
            new(OpCodes.Stloc, tmpA)
        };

        var expr = code switch
        {
            Code.Add => GenerateMbaAdd(isI64, tmpA, tmpB),
            Code.Sub => GenerateMbaSub(isI64, tmpA, tmpB),
            Code.Xor => GenerateMbaXor(isI64, tmpA, tmpB),
            Code.And => GenerateMbaAnd(isI64, tmpA, tmpB),
            Code.Or  => GenerateMbaOr(isI64, tmpA, tmpB),
            _ => null
        };

        if (expr == null) return null;
        prologue.AddRange(expr);
        return prologue.ToArray();
    }

    // a + b == (a ^ b) + 2*(a & b) == (a | b) + (a & b) == (a | b)*2 - (a ^ b)
    private Instruction[] GenerateMbaAdd(bool isI64, Local a, Local b) =>
        _random.Next(3) switch
        {
            0 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Xor),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                LdcInt(2, isI64),
                new Instruction(OpCodes.Mul),
                new Instruction(OpCodes.Add)
            },
            1 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Or),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Add)
            },
            _ => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Or),
                LdcInt(2, isI64),
                new Instruction(OpCodes.Mul),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Xor),
                new Instruction(OpCodes.Sub)
            }
        };

    // a - b == a + (~b) + 1 == (a ^ b) - 2*(~a & b)
    private Instruction[] GenerateMbaSub(bool isI64, Local a, Local b) =>
        _random.Next(2) switch
        {
            0 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.Add),
                LdcInt(1, isI64),
                new Instruction(OpCodes.Add)
            },
            _ => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Xor),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                LdcInt(2, isI64),
                new Instruction(OpCodes.Mul),
                new Instruction(OpCodes.Sub)
            }
        };

    // a ^ b == (a | b) - (a & b) == (a & ~b) | (~a & b)
    private Instruction[] GenerateMbaXor(bool isI64, Local a, Local b) =>
        _random.Next(2) switch
        {
            0 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Or),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Sub)
            },
            _ => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Or)
            }
        };

    // a & b == ~(~a | ~b) == (a | b) - (a ^ b)
    private Instruction[] GenerateMbaAnd(bool isI64, Local a, Local b) =>
        _random.Next(2) switch
        {
            0 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.Or),
                new Instruction(OpCodes.Not)
            },
            _ => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Or),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Xor),
                new Instruction(OpCodes.Sub)
            }
        };

    // a | b == ~(~a & ~b) == (a & b) + (a ^ b) == a + b - (a & b)
    private Instruction[] GenerateMbaOr(bool isI64, Local a, Local b) =>
        _random.Next(3) switch
        {
            0 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Not),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Not)
            },
            1 => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Xor),
                new Instruction(OpCodes.Add)
            },
            _ => new[]
            {
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.Add),
                new Instruction(OpCodes.Ldloc, a),
                new Instruction(OpCodes.Ldloc, b),
                new Instruction(OpCodes.And),
                new Instruction(OpCodes.Sub)
            }
        };

    // 定数 unfolding

    /// <summary>
    /// ldc.i4/i8 を「ldc(c ^ r); ldc(r); xor」のような算術評価形に展開する。
    /// 静的に定数値を抽出するには XOR/SUB の組合せを評価する必要がある。
    /// </summary>
    private Instruction[]? GenerateConstUnfold(Instruction instr, bool isI64)
    {
        if (isI64)
        {
            var value = (long)instr.Operand!;
            var r = (long)(uint)_random.Next();
            return _random.Next(2) switch
            {
                0 => new[]
                {
                    new Instruction(OpCodes.Ldc_I8, value ^ r),
                    new Instruction(OpCodes.Ldc_I8, r),
                    new Instruction(OpCodes.Xor)
                },
                _ => new[]
                {
                    new Instruction(OpCodes.Ldc_I8, unchecked(value + r)),
                    new Instruction(OpCodes.Ldc_I8, r),
                    new Instruction(OpCodes.Sub)
                }
            };
        }
        else
        {
            var value = GetLdcI4Value(instr);
            var r = _random.Next();
            return _random.Next(2) switch
            {
                0 => new[]
                {
                    Instruction.CreateLdcI4(value ^ r),
                    Instruction.CreateLdcI4(r),
                    new Instruction(OpCodes.Xor)
                },
                _ => new[]
                {
                    Instruction.CreateLdcI4(unchecked(value + r)),
                    Instruction.CreateLdcI4(r),
                    new Instruction(OpCodes.Sub)
                }
            };
        }
    }

    private static int GetLdcI4Value(Instruction instr) =>
        instr.OpCode.Code switch
        {
            Code.Ldc_I4 => (int)instr.Operand!,
            Code.Ldc_I4_S => (sbyte)instr.Operand!,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            Code.Ldc_I4_M1 => -1,
            _ => 0
        };

    private static Instruction LdcInt(int value, bool isI64) =>
        isI64
            ? new Instruction(OpCodes.Ldc_I8, (long)value)
            : Instruction.CreateLdcI4(value);

    private static bool IsInjectedHelperType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => c is '​' or '‌' or '‍' or '﻿');
    }
}
