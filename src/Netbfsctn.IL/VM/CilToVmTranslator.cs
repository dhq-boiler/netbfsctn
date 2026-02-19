using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Netbfsctn.IL.VM;

public class CilToVmTranslator
{
    private readonly ModuleDefinition _module;
    private readonly List<MemberReference> _metadataTokenTable = [];

    public CilToVmTranslator(ModuleDefinition module)
    {
        _module = module;
    }

    public List<MemberReference> MetadataTokenTable => _metadataTokenTable;

    public byte[]? Translate(MethodDefinition method)
    {
        if (!method.HasBody) return null;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var instructions = method.Body.Instructions;

        // ラベルマッピング: CIL offset → VM bytecode offset
        var labelPositions = new Dictionary<int, int>();
        var fixups = new List<(long position, int cilOffset)>();

        foreach (var instr in instructions)
        {
            labelPositions[instr.Offset] = -1; // プレースホルダー
        }

        foreach (var instr in instructions)
        {
            labelPositions[instr.Offset] = (int)ms.Position;

            switch (instr.OpCode.Code)
            {
                // 算術
                case Code.Add:
                    writer.Write((byte)VmOpCode.ADD);
                    break;
                case Code.Sub:
                    writer.Write((byte)VmOpCode.SUB);
                    break;
                case Code.Mul:
                    writer.Write((byte)VmOpCode.MUL);
                    break;
                case Code.Div:
                case Code.Div_Un:
                    writer.Write((byte)VmOpCode.DIV);
                    break;
                case Code.Rem:
                case Code.Rem_Un:
                    writer.Write((byte)VmOpCode.REM);
                    break;
                case Code.Neg:
                    writer.Write((byte)VmOpCode.NEG);
                    break;

                // 比較
                case Code.Ceq:
                    writer.Write((byte)VmOpCode.CEQ);
                    break;
                case Code.Clt:
                case Code.Clt_Un:
                    writer.Write((byte)VmOpCode.CLT);
                    break;
                case Code.Cgt:
                case Code.Cgt_Un:
                    writer.Write((byte)VmOpCode.CGT);
                    break;

                // 定数ロード
                case Code.Ldc_I4:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write((int)instr.Operand);
                    break;
                case Code.Ldc_I4_S:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write((int)(sbyte)instr.Operand);
                    break;
                case Code.Ldc_I4_0:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(0);
                    break;
                case Code.Ldc_I4_1:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(1);
                    break;
                case Code.Ldc_I4_2:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(2);
                    break;
                case Code.Ldc_I4_3:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(3);
                    break;
                case Code.Ldc_I4_4:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(4);
                    break;
                case Code.Ldc_I4_5:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(5);
                    break;
                case Code.Ldc_I4_6:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(6);
                    break;
                case Code.Ldc_I4_7:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(7);
                    break;
                case Code.Ldc_I4_8:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(8);
                    break;
                case Code.Ldc_I4_M1:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write(-1);
                    break;
                case Code.Ldc_I8:
                    writer.Write((byte)VmOpCode.LDC_I8);
                    writer.Write((long)instr.Operand);
                    break;
                case Code.Ldc_R8:
                    writer.Write((byte)VmOpCode.LDC_R8);
                    writer.Write((double)instr.Operand);
                    break;
                case Code.Ldc_R4:
                    writer.Write((byte)VmOpCode.LDC_R8);
                    writer.Write((double)(float)instr.Operand);
                    break;
                case Code.Ldstr:
                    writer.Write((byte)VmOpCode.LDSTR);
                    var str = (string)instr.Operand;
                    writer.Write(str);
                    break;
                case Code.Ldnull:
                    writer.Write((byte)VmOpCode.LDNULL);
                    break;
                case Code.Dup:
                    writer.Write((byte)VmOpCode.DUP);
                    break;
                case Code.Pop:
                    writer.Write((byte)VmOpCode.POP);
                    break;

                // ローカル変数
                case Code.Ldloc:
                case Code.Ldloc_S:
                    writer.Write((byte)VmOpCode.LDLOC);
                    writer.Write(((VariableDefinition)instr.Operand).Index);
                    break;
                case Code.Ldloc_0:
                    writer.Write((byte)VmOpCode.LDLOC);
                    writer.Write(0);
                    break;
                case Code.Ldloc_1:
                    writer.Write((byte)VmOpCode.LDLOC);
                    writer.Write(1);
                    break;
                case Code.Ldloc_2:
                    writer.Write((byte)VmOpCode.LDLOC);
                    writer.Write(2);
                    break;
                case Code.Ldloc_3:
                    writer.Write((byte)VmOpCode.LDLOC);
                    writer.Write(3);
                    break;
                case Code.Stloc:
                case Code.Stloc_S:
                    writer.Write((byte)VmOpCode.STLOC);
                    writer.Write(((VariableDefinition)instr.Operand).Index);
                    break;
                case Code.Stloc_0:
                    writer.Write((byte)VmOpCode.STLOC);
                    writer.Write(0);
                    break;
                case Code.Stloc_1:
                    writer.Write((byte)VmOpCode.STLOC);
                    writer.Write(1);
                    break;
                case Code.Stloc_2:
                    writer.Write((byte)VmOpCode.STLOC);
                    writer.Write(2);
                    break;
                case Code.Stloc_3:
                    writer.Write((byte)VmOpCode.STLOC);
                    writer.Write(3);
                    break;

                // 引数
                case Code.Ldarg:
                case Code.Ldarg_S:
                    writer.Write((byte)VmOpCode.LDARG);
                    writer.Write(((ParameterDefinition)instr.Operand).Index);
                    break;
                case Code.Ldarg_0:
                    writer.Write((byte)VmOpCode.LDARG);
                    writer.Write(0);
                    break;
                case Code.Ldarg_1:
                    writer.Write((byte)VmOpCode.LDARG);
                    writer.Write(1);
                    break;
                case Code.Ldarg_2:
                    writer.Write((byte)VmOpCode.LDARG);
                    writer.Write(2);
                    break;
                case Code.Ldarg_3:
                    writer.Write((byte)VmOpCode.LDARG);
                    writer.Write(3);
                    break;
                case Code.Starg:
                case Code.Starg_S:
                    writer.Write((byte)VmOpCode.STARG);
                    writer.Write(((ParameterDefinition)instr.Operand).Index);
                    break;

                // 分岐
                case Code.Br:
                case Code.Br_S:
                    writer.Write((byte)VmOpCode.BR);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0); // placeholder
                    break;
                case Code.Brtrue:
                case Code.Brtrue_S:
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0); // placeholder
                    break;
                case Code.Brfalse:
                case Code.Brfalse_S:
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0); // placeholder
                    break;

                // 条件分岐をbrtrue/brfalse + 比較に分解
                case Code.Beq:
                case Code.Beq_S:
                    writer.Write((byte)VmOpCode.CEQ);
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bne_Un:
                case Code.Bne_Un_S:
                    writer.Write((byte)VmOpCode.CEQ);
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Blt:
                case Code.Blt_S:
                case Code.Blt_Un:
                case Code.Blt_Un_S:
                    writer.Write((byte)VmOpCode.CLT);
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bgt:
                case Code.Bgt_S:
                case Code.Bgt_Un:
                case Code.Bgt_Un_S:
                    writer.Write((byte)VmOpCode.CGT);
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Ble:
                case Code.Ble_S:
                case Code.Ble_Un:
                case Code.Ble_Un_S:
                    writer.Write((byte)VmOpCode.CGT);
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bge:
                case Code.Bge_S:
                case Code.Bge_Un:
                case Code.Bge_Un_S:
                    writer.Write((byte)VmOpCode.CLT);
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;

                // メソッド呼び出し
                case Code.Call:
                case Code.Callvirt:
                    writer.Write((byte)VmOpCode.CALL);
                    var methodRef = (MethodReference)instr.Operand;
                    var tokenIdx = AddToTokenTable(methodRef);
                    writer.Write(tokenIdx);
                    break;

                // オブジェクト
                case Code.Newobj:
                    writer.Write((byte)VmOpCode.NEWOBJ);
                    var ctorRef = (MethodReference)instr.Operand;
                    var ctorIdx = AddToTokenTable(ctorRef);
                    writer.Write(ctorIdx);
                    break;
                case Code.Ldfld:
                    writer.Write((byte)VmOpCode.LDFLD);
                    var fldRef = (FieldReference)instr.Operand;
                    var fldIdx = AddToTokenTable(fldRef);
                    writer.Write(fldIdx);
                    break;
                case Code.Stfld:
                    writer.Write((byte)VmOpCode.STFLD);
                    var sfldRef = (FieldReference)instr.Operand;
                    var sfldIdx = AddToTokenTable(sfldRef);
                    writer.Write(sfldIdx);
                    break;
                case Code.Ldsfld:
                    writer.Write((byte)VmOpCode.LDSFLD);
                    var lsfldRef = (FieldReference)instr.Operand;
                    var lsfldIdx = AddToTokenTable(lsfldRef);
                    writer.Write(lsfldIdx);
                    break;
                case Code.Stsfld:
                    writer.Write((byte)VmOpCode.STSFLD);
                    var ssfldRef = (FieldReference)instr.Operand;
                    var ssfldIdx = AddToTokenTable(ssfldRef);
                    writer.Write(ssfldIdx);
                    break;

                // 変換
                case Code.Conv_I4:
                    writer.Write((byte)VmOpCode.CONV_I4);
                    break;
                case Code.Conv_I8:
                    writer.Write((byte)VmOpCode.CONV_I8);
                    break;
                case Code.Conv_R8:
                case Code.Conv_R4:
                    writer.Write((byte)VmOpCode.CONV_R8);
                    break;
                case Code.Conv_U1:
                case Code.Conv_U2:
                case Code.Conv_U4:
                case Code.Conv_I1:
                case Code.Conv_I2:
                    writer.Write((byte)VmOpCode.CONV_I4);
                    break;
                case Code.Box:
                    writer.Write((byte)VmOpCode.BOX);
                    var boxType = (TypeReference)instr.Operand;
                    var boxIdx = AddToTokenTable(boxType);
                    writer.Write(boxIdx);
                    break;
                case Code.Unbox_Any:
                case Code.Unbox:
                    writer.Write((byte)VmOpCode.UNBOX);
                    var unboxType = (TypeReference)instr.Operand;
                    var unboxIdx = AddToTokenTable(unboxType);
                    writer.Write(unboxIdx);
                    break;

                // 配列
                case Code.Newarr:
                    writer.Write((byte)VmOpCode.NEWARR);
                    var arrType = (TypeReference)instr.Operand;
                    var arrIdx = AddToTokenTable(arrType);
                    writer.Write(arrIdx);
                    break;
                case Code.Ldelem_Any:
                case Code.Ldelem_I4:
                case Code.Ldelem_I8:
                case Code.Ldelem_R8:
                case Code.Ldelem_Ref:
                case Code.Ldelem_U1:
                case Code.Ldelem_U2:
                case Code.Ldelem_I1:
                case Code.Ldelem_I2:
                case Code.Ldelem_R4:
                case Code.Ldelem_I:
                case Code.Ldelem_U4:
                    writer.Write((byte)VmOpCode.LDELEM);
                    break;
                case Code.Stelem_Any:
                case Code.Stelem_I4:
                case Code.Stelem_I8:
                case Code.Stelem_R8:
                case Code.Stelem_Ref:
                case Code.Stelem_I1:
                case Code.Stelem_I2:
                case Code.Stelem_I:
                    writer.Write((byte)VmOpCode.STELEM);
                    break;
                case Code.Ldlen:
                    writer.Write((byte)VmOpCode.LDLEN);
                    break;

                // 制御
                case Code.Ret:
                    writer.Write((byte)VmOpCode.RET);
                    break;
                case Code.Throw:
                    writer.Write((byte)VmOpCode.THROW);
                    break;

                case Code.Nop:
                    writer.Write((byte)VmOpCode.NOP);
                    break;

                default:
                    // サポートされていない命令 → 変換不可
                    return null;
            }
        }

        // 分岐先のフィックスアップ
        var bytes = ms.ToArray();
        foreach (var (position, cilOffset) in fixups)
        {
            if (!labelPositions.TryGetValue(cilOffset, out var vmOffset) || vmOffset < 0)
                return null; // 分岐先が解決できない

            BitConverter.GetBytes(vmOffset).CopyTo(bytes, (int)position);
        }

        return bytes;
    }

    private int AddToTokenTable(MemberReference member)
    {
        var idx = _metadataTokenTable.IndexOf(member);
        if (idx >= 0) return idx;
        _metadataTokenTable.Add(member);
        return _metadataTokenTable.Count - 1;
    }

    private int AddToTokenTable(TypeReference type)
    {
        // TypeReference を MemberReference として格納
        for (var i = 0; i < _metadataTokenTable.Count; i++)
        {
            if (_metadataTokenTable[i] is TypeReference tr && tr.FullName == type.FullName)
                return i;
        }
        _metadataTokenTable.Add(type);
        return _metadataTokenTable.Count - 1;
    }
}
