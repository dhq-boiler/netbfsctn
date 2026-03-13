using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Netbfsctn.IL.VM;

public class VmInterpreterInjector
{
    public TypeDefUser InjectInterpreter(ModuleDef module)
    {
        var importer = new Importer(module);

        var interpreterType = new TypeDefUser("", "\u200B\u200D\u200C\u200D", module.CorLibTypes.Object.TypeDefOrRef);
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

        BuildExecuteBody(executeMethod, module, importer);
        executeMethod.Body.KeepOldMaxStack = true;

        interpreterType.Methods.Add(executeMethod);
        module.Types.Add(interpreterType);

        return interpreterType;
    }

    private static void BuildExecuteBody(MethodDefUser method, ModuleDef module, Importer importer)
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
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[5]));

        // RET check
        var notRet = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[5]));
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.RET));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notRet));

        var retWithPop = new Instruction(OpCodes.Ldloc_0);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackCount));
        body.Instructions.Add(new Instruction(OpCodes.Brtrue, retWithPop));
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ret));
        body.Instructions.Add(retWithPop);
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        // LDC_I4 check
        var notLdcI4 = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notRet);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.LDC_I4));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notLdcI4));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Box, module.CorLibTypes.Int32.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // LDSTR check
        var notLdstr = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notLdcI4);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.LDSTR));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notLdstr));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readString));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // ADD check
        var notAdd = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notLdstr);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.ADD));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notAdd));
        EmitBinaryOp(body, stackPop, stackPush, module, OpCodes.Add);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // SUB check
        var notSub = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notAdd);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.SUB));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notSub));
        EmitBinaryOp(body, stackPop, stackPush, module, OpCodes.Sub);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // MUL check
        var notMul = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notSub);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.MUL));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notMul));
        EmitBinaryOp(body, stackPop, stackPush, module, OpCodes.Mul);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // LDLOC check
        var notLdloc = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notMul);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.LDLOC));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notLdloc));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_Ref));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // STLOC check
        var notStloc = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notLdloc);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.STLOC));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notStloc));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Stelem_Ref));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // LDARG check
        var notLdarg = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notStloc);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.LDARG));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notLdarg));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_Ref));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPush));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // BR check
        var notBr = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notLdarg);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.BR));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notBr));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I8));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, setPosition));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // POP check
        var notPop = new Instruction(OpCodes.Ldloc, body.Variables[5]);
        body.Instructions.Add(notBr);
        body.Instructions.Add(Instruction.CreateLdcI4((int)VmOpCode.POP));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, notPop));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, stackPop));
        body.Instructions.Add(new Instruction(OpCodes.Pop));
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // default: continue loop
        body.Instructions.Add(notPop);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // return null
        body.Instructions.Add(retNull);
        body.Instructions.Add(new Instruction(OpCodes.Ret));
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
