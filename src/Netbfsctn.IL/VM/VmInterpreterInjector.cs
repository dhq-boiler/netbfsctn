using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Netbfsctn.IL.VM;

public class VmInterpreterInjector
{
    public TypeDefUser InjectInterpreter(ModuleDef module, VmIsa isa)
    {
        var importer = new Importer(module);

        var interpreterType = new TypeDefUser("", "​‍‌‍", module.CorLibTypes.Object.TypeDefOrRef);
        interpreterType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);
        var objectArraySig = new SZArraySig(module.CorLibTypes.Object);

        var executeMethod = new MethodDefUser(
            "E",
            MethodSig.CreateStatic(module.CorLibTypes.Object, byteArraySig, objectArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);

        BuildExecuteBody(executeMethod, module, importer, isa);
        executeMethod.Body.KeepOldMaxStack = true;

        interpreterType.Methods.Add(executeMethod);
        module.Types.Add(interpreterType);

        return interpreterType;
    }

    private static void BuildExecuteBody(MethodDefUser method, ModuleDef module, Importer importer, VmIsa isa)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        var stackTypeDef = typeof(Stack<object>);

        body.Variables.Add(new Local(importer.ImportAsTypeSig(stackTypeDef))); // 0: stack
        body.Variables.Add(new Local(new SZArraySig(module.CorLibTypes.Object))); // 1: locals
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 2: pc (unused)
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(MemoryStream)))); // 3: ms
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(BinaryReader)))); // 4: reader
        body.Variables.Add(new Local(module.CorLibTypes.Byte)); // 5: opcode
        body.Variables.Add(new Local(module.CorLibTypes.Object)); // 6: temp a
        body.Variables.Add(new Local(module.CorLibTypes.Object)); // 7: temp b

        var opcodeVar = body.Variables[5];

        var stackCtor = importer.Import(stackTypeDef.GetConstructor(Type.EmptyTypes)!);
        var stackPop = importer.Import(stackTypeDef.GetMethod("Pop")!);
        var stackCount = importer.Import(stackTypeDef.GetProperty("Count")!.GetGetMethod()!);
        var stackPush = importer.Import(stackTypeDef.GetMethod("Push")!);

        var msCtor = importer.Import(typeof(MemoryStream).GetConstructor([typeof(byte[])])!);
        var brCtor = importer.Import(typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        var getPosition = importer.Import(typeof(Stream).GetProperty("Position")!.GetGetMethod()!);
        var getLength = importer.Import(typeof(Stream).GetProperty("Length")!.GetGetMethod()!);
        var setPosition = importer.Import(typeof(Stream).GetProperty("Position")!.GetSetMethod()!);
        var readByte = importer.Import(typeof(BinaryReader).GetMethod("ReadByte")!);
        var readInt32 = importer.Import(typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readString = importer.Import(typeof(BinaryReader).GetMethod("ReadString")!);

        // stack = new Stack<object>()
        body.Instructions.Add(new Instruction(OpCodes.Newobj, stackCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_0));

        // locals = new object[64]
        body.Instructions.Add(Instruction.CreateLdcI4(64));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        // ms = new MemoryStream(bc)
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Newobj, msCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_3));

        // reader = new BinaryReader(ms)
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Newobj, brCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        // main loop
        var loopStart = new Instruction(OpCodes.Ldloc_3);
        var retNull = new Instruction(OpCodes.Ldnull);

        body.Instructions.Add(loopStart);
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getPosition));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getLength));
        body.Instructions.Add(new Instruction(OpCodes.Bge, retNull));

        // opcode = reader.ReadByte()
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readByte));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, opcodeVar));

        // --- RET dispatch (special: ends with return) ---
        var notRet = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerRet = new Instruction(OpCodes.Nop);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, opcodeVar));
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.RET), notRet, handlerRet);

        body.Instructions.Add(handlerRet);
        var retWithPop = new Instruction(OpCodes.Ldloc_0);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackCount));
        body.Instructions.Add(new Instruction(OpCodes.Brtrue, retWithPop));
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        body.Instructions.Add(retWithPop);
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        // --- LDC_I4 dispatch ---
        var notLdcI4 = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerLdcI4 = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notRet);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.LDC_I4), notLdcI4, handlerLdcI4);
        body.Instructions.Add(handlerLdcI4);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Box, module.CorLibTypes.Int32.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- LDSTR dispatch ---
        var notLdstr = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerLdstr = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notLdcI4);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.LDSTR), notLdstr, handlerLdstr);
        body.Instructions.Add(handlerLdstr);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readString));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- ADD dispatch ---
        var notAdd = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerAdd = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notLdstr);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.ADD), notAdd, handlerAdd);
        body.Instructions.Add(handlerAdd);
        EmitBinaryOp(body, stackPop, stackPush, module, OpCodes.Add);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- SUB dispatch ---
        var notSub = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerSub = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notAdd);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.SUB), notSub, handlerSub);
        body.Instructions.Add(handlerSub);
        EmitBinaryOp(body, stackPop, stackPush, module, OpCodes.Sub);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- MUL dispatch ---
        var notMul = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerMul = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notSub);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.MUL), notMul, handlerMul);
        body.Instructions.Add(handlerMul);
        EmitBinaryOp(body, stackPop, stackPush, module, OpCodes.Mul);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- LDLOC dispatch ---
        var notLdloc = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerLdloc = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notMul);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.LDLOC), notLdloc, handlerLdloc);
        body.Instructions.Add(handlerLdloc);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_Ref));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- STLOC dispatch ---
        var notStloc = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerStloc = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notLdloc);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.STLOC), notStloc, handlerStloc);
        body.Instructions.Add(handlerStloc);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Stelem_Ref));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- LDARG dispatch ---
        var notLdarg = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerLdarg = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notStloc);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.LDARG), notLdarg, handlerLdarg);
        body.Instructions.Add(handlerLdarg);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_Ref));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- BR dispatch ---
        var notBr = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerBr = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notLdarg);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.BR), notBr, handlerBr);
        body.Instructions.Add(handlerBr);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I8));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, setPosition));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // --- POP dispatch ---
        var notPop = new Instruction(OpCodes.Ldloc, opcodeVar);
        var handlerPop = new Instruction(OpCodes.Nop);
        body.Instructions.Add(notBr);
        EmitDispatch(body, opcodeVar, isa.GetAllEncodings(VmOpCode.POP), notPop, handlerPop);
        body.Instructions.Add(handlerPop);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Pop));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // default: continue loop
        body.Instructions.Add(notPop);
        body.Instructions.Add(new Instruction(OpCodes.Pop)); // drop the unused opcode loaded by notPop
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // return null
        body.Instructions.Add(retNull);
        body.Instructions.Add(new Instruction(OpCodes.Ret));
    }

    /// <summary>
    /// 1 つの opcode に対する dispatch チェックを生成する。
    /// 直前の命令が opcode 値を stack にロードしていることを前提とする。
    /// 単一エンコーディングなら従来通り 1 回の Bne_Un で次の op に飛ぶ。
    /// 複数エンコーディング (polymorphic) なら、最後以外は Beq でハンドラに飛び、
    /// 最後だけ Bne_Un で次の op に飛ぶ。
    /// </summary>
    private static void EmitDispatch(
        CilBody body, Local opcodeVar,
        IReadOnlyList<byte> encodings,
        Instruction noMatchTarget,
        Instruction handlerEntry)
    {
        for (var i = 0; i < encodings.Count - 1; i++)
        {
            body.Instructions.Add(Instruction.CreateLdcI4(encodings[i]));
            body.Instructions.Add(new Instruction(OpCodes.Beq, handlerEntry));
            body.Instructions.Add(new Instruction(OpCodes.Ldloc, opcodeVar));
        }
        body.Instructions.Add(Instruction.CreateLdcI4(encodings[^1]));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, noMatchTarget));
    }

    private static void EmitBinaryOp(CilBody body, IMethod stackPop, IMethod stackPush,
        ModuleDef module, OpCode arithOp)
    {
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[7]));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[6]));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[6]));
        body.Instructions.Add(new Instruction(OpCodes.Unbox_Any, module.CorLibTypes.Int32.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[7]));
        body.Instructions.Add(new Instruction(OpCodes.Unbox_Any, module.CorLibTypes.Int32.TypeDefOrRef));
        body.Instructions.Add(new Instruction(arithOp));
        body.Instructions.Add(new Instruction(OpCodes.Box, module.CorLibTypes.Int32.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
    }
}
