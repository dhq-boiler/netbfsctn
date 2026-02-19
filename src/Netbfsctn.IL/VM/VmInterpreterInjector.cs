using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Netbfsctn.IL.VM;

public class VmInterpreterInjector
{
    public TypeDefinition InjectInterpreter(ModuleDefinition module)
    {
        var interpreterType = new TypeDefinition(
            "",
            "\u200B\u200D\u200C\u200D",
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract,
            module.ImportReference(typeof(object)));

        // static object Execute(byte[] bytecode, object[] args, string[] tokenTable)
        var executeMethod = new MethodDefinition(
            "E",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(object)));

        executeMethod.Parameters.Add(new ParameterDefinition("bc", ParameterAttributes.None,
            module.ImportReference(typeof(byte[]))));
        executeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None,
            module.ImportReference(typeof(object[]))));
        executeMethod.Parameters.Add(new ParameterDefinition("tokens", ParameterAttributes.None,
            module.ImportReference(typeof(string[]))));

        BuildExecuteBody(executeMethod, module);

        interpreterType.Methods.Add(executeMethod);
        module.Types.Add(interpreterType);

        return interpreterType;
    }

    private static void BuildExecuteBody(MethodDefinition method, ModuleDefinition module)
    {
        var il = method.Body.GetILProcessor();
        method.Body.InitLocals = true;

        // ローカル変数:
        // 0: Stack<object> stack
        // 1: object[] locals (64 slots)
        // 2: int pc
        // 3: BinaryReader reader
        // 4: byte opcode
        // 5: object a, 6: object b, 7: object result

        var stackType = module.ImportReference(typeof(Stack<object>));
        var stackTypeDef = typeof(Stack<object>);
        method.Body.Variables.Add(new VariableDefinition(stackType)); // 0
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(object[])))); // 1
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 2: pc
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(MemoryStream)))); // 3: ms
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(BinaryReader)))); // 4: reader
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(byte)))); // 5: opcode
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(object)))); // 6: temp a
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(object)))); // 7: temp b

        // stack = new Stack<object>();
        var stackCtor = module.ImportReference(stackTypeDef.GetConstructor(Type.EmptyTypes)!);
        il.Append(il.Create(OpCodes.Newobj, stackCtor));
        il.Append(il.Create(OpCodes.Stloc_0));

        // locals = new object[64];
        il.Append(il.Create(OpCodes.Ldc_I4, 64));
        il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(object))));
        il.Append(il.Create(OpCodes.Stloc_1));

        // ms = new MemoryStream(bc);
        var msCtor = module.ImportReference(typeof(MemoryStream).GetConstructor([typeof(byte[])])!);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Newobj, msCtor));
        il.Append(il.Create(OpCodes.Stloc_3));

        // reader = new BinaryReader(ms);
        var brCtor = module.ImportReference(typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        il.Append(il.Create(OpCodes.Ldloc_3));
        il.Append(il.Create(OpCodes.Newobj, brCtor));
        il.Append(il.Create(OpCodes.Stloc, 4));

        // メインループ: while (ms.Position < ms.Length)
        var loopStart = il.Create(OpCodes.Ldloc_3);
        var retNull = il.Create(OpCodes.Ldnull);

        il.Append(loopStart);
        var getPosition = module.ImportReference(typeof(Stream).GetProperty("Position")!.GetGetMethod()!);
        var getLength = module.ImportReference(typeof(Stream).GetProperty("Length")!.GetGetMethod()!);
        il.Append(il.Create(OpCodes.Callvirt, getPosition));
        il.Append(il.Create(OpCodes.Ldloc_3));
        il.Append(il.Create(OpCodes.Callvirt, getLength));
        il.Append(il.Create(OpCodes.Bge, retNull));

        // opcode = reader.ReadByte();
        var readByte = module.ImportReference(typeof(BinaryReader).GetMethod("ReadByte")!);
        var readInt32 = module.ImportReference(typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readInt64 = module.ImportReference(typeof(BinaryReader).GetMethod("ReadInt64")!);
        var readDouble = module.ImportReference(typeof(BinaryReader).GetMethod("ReadDouble")!);
        var readString = module.ImportReference(typeof(BinaryReader).GetMethod("ReadString")!);

        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readByte));
        il.Append(il.Create(OpCodes.Stloc, 5));

        // switch(opcode) - 簡略化: 主要な命令のみ処理
        // RET (0xF0) チェック
        var notRet = il.Create(OpCodes.Ldloc, 5);
        il.Append(il.Create(OpCodes.Ldloc, 5));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.RET));
        il.Append(il.Create(OpCodes.Bne_Un, notRet));

        // RET: return stack.Count > 0 ? stack.Pop() : null
        var stackPop = module.ImportReference(stackTypeDef.GetMethod("Pop")!);
        var stackCount = module.ImportReference(stackTypeDef.GetProperty("Count")!.GetGetMethod()!);
        var stackPush = module.ImportReference(stackTypeDef.GetMethod("Push")!);

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackCount));

        var retWithPop = il.Create(OpCodes.Ldloc_0);
        il.Append(il.Create(OpCodes.Brtrue, retWithPop));
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ret));

        il.Append(retWithPop);
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Ret));

        // LDC_I4 (0x20) チェック
        var notLdcI4 = il.Create(OpCodes.Ldloc, 5);
        il.Append(notRet);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.LDC_I4));
        il.Append(il.Create(OpCodes.Bne_Un, notLdcI4));

        // stack.Push(reader.ReadInt32())
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Box, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // LDSTR (0x23) チェック
        var notLdstr = il.Create(OpCodes.Ldloc, 5);
        il.Append(notLdcI4);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.LDSTR));
        il.Append(il.Create(OpCodes.Bne_Un, notLdstr));

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readString));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // ADD (0x01) チェック
        var notAdd = il.Create(OpCodes.Ldloc, 5);
        il.Append(notLdstr);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.ADD));
        il.Append(il.Create(OpCodes.Bne_Un, notAdd));

        // b = stack.Pop(); a = stack.Pop(); stack.Push((int)a + (int)b);
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stloc, 7)); // b
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stloc, 6)); // a
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldloc, 6));
        il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Ldloc, 7));
        il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Box, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // SUB (0x02) チェック
        var notSub = il.Create(OpCodes.Ldloc, 5);
        il.Append(notAdd);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.SUB));
        il.Append(il.Create(OpCodes.Bne_Un, notSub));

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stloc, 7));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stloc, 6));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldloc, 6));
        il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Ldloc, 7));
        il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Sub));
        il.Append(il.Create(OpCodes.Box, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // MUL (0x03) チェック
        var notMul = il.Create(OpCodes.Ldloc, 5);
        il.Append(notSub);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.MUL));
        il.Append(il.Create(OpCodes.Bne_Un, notMul));

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stloc, 7));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stloc, 6));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldloc, 6));
        il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Ldloc, 7));
        il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Box, module.ImportReference(typeof(int))));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // LDLOC (0x30) チェック
        var notLdloc = il.Create(OpCodes.Ldloc, 5);
        il.Append(notMul);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.LDLOC));
        il.Append(il.Create(OpCodes.Bne_Un, notLdloc));

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldloc_1)); // locals array
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // STLOC (0x31) チェック
        var notStloc = il.Create(OpCodes.Ldloc, 5);
        il.Append(notLdloc);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.STLOC));
        il.Append(il.Create(OpCodes.Bne_Un, notStloc));

        il.Append(il.Create(OpCodes.Ldloc_1)); // locals array
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Stelem_Ref));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // LDARG (0x32) チェック
        var notLdarg = il.Create(OpCodes.Ldloc, 5);
        il.Append(notStloc);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.LDARG));
        il.Append(il.Create(OpCodes.Bne_Un, notLdarg));

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldarg_1)); // args array
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Callvirt, stackPush));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // BR (0x40) チェック - 無条件分岐
        var notBr = il.Create(OpCodes.Ldloc, 5);
        il.Append(notLdarg);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.BR));
        il.Append(il.Create(OpCodes.Bne_Un, notBr));

        var setPosition = module.ImportReference(typeof(Stream).GetProperty("Position")!.GetSetMethod()!);
        il.Append(il.Create(OpCodes.Ldloc_3)); // ms
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Conv_I8));
        il.Append(il.Create(OpCodes.Callvirt, setPosition));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // POP (0x26) チェック
        var notPop = il.Create(OpCodes.Ldloc, 5);
        il.Append(notBr);
        il.Append(il.Create(OpCodes.Ldc_I4, (int)VmOpCode.POP));
        il.Append(il.Create(OpCodes.Bne_Un, notPop));

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, stackPop));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Br, loopStart));

        // NOP (0xFF) チェック and default - just continue loop
        il.Append(notPop);
        // 未知の命令はスキップ (reader.ReadInt32 でオペランドを消費)
        // ただし一部命令はオペランドなしなのでそのまま進む
        il.Append(il.Create(OpCodes.Br, loopStart));

        // return null (ループ終了)
        il.Append(retNull);
        il.Append(il.Create(OpCodes.Ret));
    }
}
