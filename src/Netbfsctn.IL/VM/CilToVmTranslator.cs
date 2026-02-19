using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Netbfsctn.IL.VM;

public class CilToVmTranslator
{
    private readonly ModuleDef _module;
    private readonly List<IFullName> _metadataTokenTable = [];

    public CilToVmTranslator(ModuleDef module)
    {
        _module = module;
    }

    public List<IFullName> MetadataTokenTable => _metadataTokenTable;

    public byte[]? Translate(MethodDef method)
    {
        if (!method.HasBody) return null;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var instructions = method.Body.Instructions;

        var labelPositions = new Dictionary<uint, int>();
        var fixups = new List<(long position, uint cilOffset)>();

        foreach (var instr in instructions)
            labelPositions[instr.Offset] = -1;

        foreach (var instr in instructions)
        {
            labelPositions[instr.Offset] = (int)ms.Position;

            switch (instr.OpCode.Code)
            {
                case Code.Add:
                    writer.Write((byte)VmOpCode.ADD);
                    break;
                case Code.Sub:
                    writer.Write((byte)VmOpCode.SUB);
                    break;
                case Code.Mul:
                    writer.Write((byte)VmOpCode.MUL);
                    break;
                case Code.Div or Code.Div_Un:
                    writer.Write((byte)VmOpCode.DIV);
                    break;
                case Code.Rem or Code.Rem_Un:
                    writer.Write((byte)VmOpCode.REM);
                    break;
                case Code.Neg:
                    writer.Write((byte)VmOpCode.NEG);
                    break;
                case Code.Ceq:
                    writer.Write((byte)VmOpCode.CEQ);
                    break;
                case Code.Clt or Code.Clt_Un:
                    writer.Write((byte)VmOpCode.CLT);
                    break;
                case Code.Cgt or Code.Cgt_Un:
                    writer.Write((byte)VmOpCode.CGT);
                    break;

                case Code.Ldc_I4:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write((int)instr.Operand);
                    break;
                case Code.Ldc_I4_S:
                    writer.Write((byte)VmOpCode.LDC_I4);
                    writer.Write((int)(sbyte)instr.Operand);
                    break;
                case Code.Ldc_I4_0: WriteLdcI4(writer, 0); break;
                case Code.Ldc_I4_1: WriteLdcI4(writer, 1); break;
                case Code.Ldc_I4_2: WriteLdcI4(writer, 2); break;
                case Code.Ldc_I4_3: WriteLdcI4(writer, 3); break;
                case Code.Ldc_I4_4: WriteLdcI4(writer, 4); break;
                case Code.Ldc_I4_5: WriteLdcI4(writer, 5); break;
                case Code.Ldc_I4_6: WriteLdcI4(writer, 6); break;
                case Code.Ldc_I4_7: WriteLdcI4(writer, 7); break;
                case Code.Ldc_I4_8: WriteLdcI4(writer, 8); break;
                case Code.Ldc_I4_M1: WriteLdcI4(writer, -1); break;

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
                    writer.Write((string)instr.Operand);
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

                case Code.Ldloc or Code.Ldloc_S:
                    writer.Write((byte)VmOpCode.LDLOC);
                    writer.Write(((Local)instr.Operand).Index);
                    break;
                case Code.Ldloc_0: WriteLdloc(writer, 0); break;
                case Code.Ldloc_1: WriteLdloc(writer, 1); break;
                case Code.Ldloc_2: WriteLdloc(writer, 2); break;
                case Code.Ldloc_3: WriteLdloc(writer, 3); break;

                case Code.Stloc or Code.Stloc_S:
                    writer.Write((byte)VmOpCode.STLOC);
                    writer.Write(((Local)instr.Operand).Index);
                    break;
                case Code.Stloc_0: WriteStloc(writer, 0); break;
                case Code.Stloc_1: WriteStloc(writer, 1); break;
                case Code.Stloc_2: WriteStloc(writer, 2); break;
                case Code.Stloc_3: WriteStloc(writer, 3); break;

                case Code.Ldarg or Code.Ldarg_S:
                    writer.Write((byte)VmOpCode.LDARG);
                    writer.Write(((Parameter)instr.Operand).Index);
                    break;
                case Code.Ldarg_0: WriteLdarg(writer, 0); break;
                case Code.Ldarg_1: WriteLdarg(writer, 1); break;
                case Code.Ldarg_2: WriteLdarg(writer, 2); break;
                case Code.Ldarg_3: WriteLdarg(writer, 3); break;

                case Code.Starg or Code.Starg_S:
                    writer.Write((byte)VmOpCode.STARG);
                    writer.Write(((Parameter)instr.Operand).Index);
                    break;

                case Code.Br or Code.Br_S:
                    writer.Write((byte)VmOpCode.BR);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Brtrue or Code.Brtrue_S:
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Brfalse or Code.Brfalse_S:
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;

                case Code.Beq or Code.Beq_S:
                    writer.Write((byte)VmOpCode.CEQ);
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bne_Un or Code.Bne_Un_S:
                    writer.Write((byte)VmOpCode.CEQ);
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S:
                    writer.Write((byte)VmOpCode.CLT);
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S:
                    writer.Write((byte)VmOpCode.CGT);
                    writer.Write((byte)VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S:
                    writer.Write((byte)VmOpCode.CGT);
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S:
                    writer.Write((byte)VmOpCode.CLT);
                    writer.Write((byte)VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;

                case Code.Call or Code.Callvirt:
                    writer.Write((byte)VmOpCode.CALL);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Newobj:
                    writer.Write((byte)VmOpCode.NEWOBJ);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Ldfld:
                    writer.Write((byte)VmOpCode.LDFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Stfld:
                    writer.Write((byte)VmOpCode.STFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Ldsfld:
                    writer.Write((byte)VmOpCode.LDSFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Stsfld:
                    writer.Write((byte)VmOpCode.STSFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;

                case Code.Conv_I4:
                    writer.Write((byte)VmOpCode.CONV_I4);
                    break;
                case Code.Conv_I8:
                    writer.Write((byte)VmOpCode.CONV_I8);
                    break;
                case Code.Conv_R8 or Code.Conv_R4:
                    writer.Write((byte)VmOpCode.CONV_R8);
                    break;
                case Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4
                    or Code.Conv_I1 or Code.Conv_I2:
                    writer.Write((byte)VmOpCode.CONV_I4);
                    break;
                case Code.Box:
                    writer.Write((byte)VmOpCode.BOX);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Unbox_Any or Code.Unbox:
                    writer.Write((byte)VmOpCode.UNBOX);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;

                case Code.Newarr:
                    writer.Write((byte)VmOpCode.NEWARR);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Ldelem or Code.Ldelem_I4 or Code.Ldelem_I8
                    or Code.Ldelem_R8 or Code.Ldelem_Ref or Code.Ldelem_U1
                    or Code.Ldelem_U2 or Code.Ldelem_I1 or Code.Ldelem_I2
                    or Code.Ldelem_R4 or Code.Ldelem_I or Code.Ldelem_U4:
                    writer.Write((byte)VmOpCode.LDELEM);
                    break;
                case Code.Stelem or Code.Stelem_I4 or Code.Stelem_I8
                    or Code.Stelem_R8 or Code.Stelem_Ref or Code.Stelem_I1
                    or Code.Stelem_I2 or Code.Stelem_I:
                    writer.Write((byte)VmOpCode.STELEM);
                    break;
                case Code.Ldlen:
                    writer.Write((byte)VmOpCode.LDLEN);
                    break;

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
                    return null;
            }
        }

        var bytes = ms.ToArray();
        foreach (var (position, cilOffset) in fixups)
        {
            if (!labelPositions.TryGetValue(cilOffset, out var vmOffset) || vmOffset < 0)
                return null;
            BitConverter.GetBytes(vmOffset).CopyTo(bytes, (int)position);
        }

        return bytes;
    }

    private int AddToTokenTable(IFullName member)
    {
        for (var i = 0; i < _metadataTokenTable.Count; i++)
            if (_metadataTokenTable[i].FullName == member.FullName)
                return i;
        _metadataTokenTable.Add(member);
        return _metadataTokenTable.Count - 1;
    }

    private static void WriteLdcI4(BinaryWriter w, int val) { w.Write((byte)VmOpCode.LDC_I4); w.Write(val); }
    private static void WriteLdloc(BinaryWriter w, int idx) { w.Write((byte)VmOpCode.LDLOC); w.Write(idx); }
    private static void WriteStloc(BinaryWriter w, int idx) { w.Write((byte)VmOpCode.STLOC); w.Write(idx); }
    private static void WriteLdarg(BinaryWriter w, int idx) { w.Write((byte)VmOpCode.LDARG); w.Write(idx); }
}
