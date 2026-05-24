using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Netbfsctn.IL.VM;

public class CilToVmTranslator
{
    private readonly ModuleDef _module;
    private readonly VmIsa _isa;
    private readonly List<IFullName> _metadataTokenTable = [];

    public CilToVmTranslator(ModuleDef module, VmIsa isa)
    {
        _module = module;
        _isa = isa;
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
                    Write(writer, VmOpCode.ADD);
                    break;
                case Code.Sub:
                    Write(writer, VmOpCode.SUB);
                    break;
                case Code.Mul:
                    Write(writer, VmOpCode.MUL);
                    break;
                case Code.Div or Code.Div_Un:
                    Write(writer, VmOpCode.DIV);
                    break;
                case Code.Rem or Code.Rem_Un:
                    Write(writer, VmOpCode.REM);
                    break;
                case Code.Neg:
                    Write(writer, VmOpCode.NEG);
                    break;
                case Code.Ceq:
                    Write(writer, VmOpCode.CEQ);
                    break;
                case Code.Clt or Code.Clt_Un:
                    Write(writer, VmOpCode.CLT);
                    break;
                case Code.Cgt or Code.Cgt_Un:
                    Write(writer, VmOpCode.CGT);
                    break;

                case Code.Ldc_I4:
                    Write(writer, VmOpCode.LDC_I4);
                    writer.Write((int)instr.Operand);
                    break;
                case Code.Ldc_I4_S:
                    Write(writer, VmOpCode.LDC_I4);
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
                    Write(writer, VmOpCode.LDC_I8);
                    writer.Write((long)instr.Operand);
                    break;
                case Code.Ldc_R8:
                    Write(writer, VmOpCode.LDC_R8);
                    writer.Write((double)instr.Operand);
                    break;
                case Code.Ldc_R4:
                    Write(writer, VmOpCode.LDC_R8);
                    writer.Write((double)(float)instr.Operand);
                    break;
                case Code.Ldstr:
                    Write(writer, VmOpCode.LDSTR);
                    writer.Write((string)instr.Operand);
                    break;
                case Code.Ldnull:
                    Write(writer, VmOpCode.LDNULL);
                    break;
                case Code.Dup:
                    Write(writer, VmOpCode.DUP);
                    break;
                case Code.Pop:
                    Write(writer, VmOpCode.POP);
                    break;

                case Code.Ldloc or Code.Ldloc_S:
                    Write(writer, VmOpCode.LDLOC);
                    writer.Write(((Local)instr.Operand).Index);
                    break;
                case Code.Ldloc_0: WriteLdloc(writer, 0); break;
                case Code.Ldloc_1: WriteLdloc(writer, 1); break;
                case Code.Ldloc_2: WriteLdloc(writer, 2); break;
                case Code.Ldloc_3: WriteLdloc(writer, 3); break;

                case Code.Stloc or Code.Stloc_S:
                    Write(writer, VmOpCode.STLOC);
                    writer.Write(((Local)instr.Operand).Index);
                    break;
                case Code.Stloc_0: WriteStloc(writer, 0); break;
                case Code.Stloc_1: WriteStloc(writer, 1); break;
                case Code.Stloc_2: WriteStloc(writer, 2); break;
                case Code.Stloc_3: WriteStloc(writer, 3); break;

                case Code.Ldarg or Code.Ldarg_S:
                    Write(writer, VmOpCode.LDARG);
                    writer.Write(((Parameter)instr.Operand).Index);
                    break;
                case Code.Ldarg_0: WriteLdarg(writer, 0); break;
                case Code.Ldarg_1: WriteLdarg(writer, 1); break;
                case Code.Ldarg_2: WriteLdarg(writer, 2); break;
                case Code.Ldarg_3: WriteLdarg(writer, 3); break;

                case Code.Starg or Code.Starg_S:
                    Write(writer, VmOpCode.STARG);
                    writer.Write(((Parameter)instr.Operand).Index);
                    break;

                case Code.Br or Code.Br_S:
                    Write(writer, VmOpCode.BR);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Brtrue or Code.Brtrue_S:
                    Write(writer, VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Brfalse or Code.Brfalse_S:
                    Write(writer, VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;

                case Code.Beq or Code.Beq_S:
                    Write(writer, VmOpCode.CEQ);
                    Write(writer, VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bne_Un or Code.Bne_Un_S:
                    Write(writer, VmOpCode.CEQ);
                    Write(writer, VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S:
                    Write(writer, VmOpCode.CLT);
                    Write(writer, VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S:
                    Write(writer, VmOpCode.CGT);
                    Write(writer, VmOpCode.BRTRUE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S:
                    Write(writer, VmOpCode.CGT);
                    Write(writer, VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;
                case Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S:
                    Write(writer, VmOpCode.CLT);
                    Write(writer, VmOpCode.BRFALSE);
                    fixups.Add((ms.Position, ((Instruction)instr.Operand).Offset));
                    writer.Write(0);
                    break;

                case Code.Call or Code.Callvirt:
                    Write(writer, VmOpCode.CALL);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Newobj:
                    Write(writer, VmOpCode.NEWOBJ);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Ldfld:
                    Write(writer, VmOpCode.LDFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Stfld:
                    Write(writer, VmOpCode.STFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Ldsfld:
                    Write(writer, VmOpCode.LDSFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Stsfld:
                    Write(writer, VmOpCode.STSFLD);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;

                case Code.Conv_I4:
                    Write(writer, VmOpCode.CONV_I4);
                    break;
                case Code.Conv_I8:
                    Write(writer, VmOpCode.CONV_I8);
                    break;
                case Code.Conv_R8 or Code.Conv_R4:
                    Write(writer, VmOpCode.CONV_R8);
                    break;
                case Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4
                    or Code.Conv_I1 or Code.Conv_I2:
                    Write(writer, VmOpCode.CONV_I4);
                    break;
                case Code.Box:
                    Write(writer, VmOpCode.BOX);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Unbox_Any or Code.Unbox:
                    Write(writer, VmOpCode.UNBOX);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;

                case Code.Newarr:
                    Write(writer, VmOpCode.NEWARR);
                    writer.Write(AddToTokenTable((IFullName)instr.Operand));
                    break;
                case Code.Ldelem or Code.Ldelem_I4 or Code.Ldelem_I8
                    or Code.Ldelem_R8 or Code.Ldelem_Ref or Code.Ldelem_U1
                    or Code.Ldelem_U2 or Code.Ldelem_I1 or Code.Ldelem_I2
                    or Code.Ldelem_R4 or Code.Ldelem_I or Code.Ldelem_U4:
                    Write(writer, VmOpCode.LDELEM);
                    break;
                case Code.Stelem or Code.Stelem_I4 or Code.Stelem_I8
                    or Code.Stelem_R8 or Code.Stelem_Ref or Code.Stelem_I1
                    or Code.Stelem_I2 or Code.Stelem_I:
                    Write(writer, VmOpCode.STELEM);
                    break;
                case Code.Ldlen:
                    Write(writer, VmOpCode.LDLEN);
                    break;

                case Code.Ret:
                    Write(writer, VmOpCode.RET);
                    break;
                case Code.Throw:
                    Write(writer, VmOpCode.THROW);
                    break;
                case Code.Nop:
                    Write(writer, VmOpCode.NOP);
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

    private void Write(BinaryWriter w, VmOpCode op) => w.Write(_isa.PickEncoding(op));
    private void WriteLdcI4(BinaryWriter w, int val) { Write(w, VmOpCode.LDC_I4); w.Write(val); }
    private void WriteLdloc(BinaryWriter w, int idx) { Write(w, VmOpCode.LDLOC); w.Write(idx); }
    private void WriteStloc(BinaryWriter w, int idx) { Write(w, VmOpCode.STLOC); w.Write(idx); }
    private void WriteLdarg(BinaryWriter w, int idx) { Write(w, VmOpCode.LDARG); w.Write(idx); }
}
