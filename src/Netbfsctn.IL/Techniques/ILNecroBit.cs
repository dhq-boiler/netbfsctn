using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILNecroBit : IObfuscationTechnique<ModuleDef>
{
    public string Name => "NecroBit (IL)";

    private const string ResourceName = "\u200C\u200B\u200D";
    private const byte XorKey = 0xC7;

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);

        // ヘルパー型を注入
        var helperType = InjectNecroBitHelper(module, importer);
        var restoreMethod = helperType.Methods.First(m => m.Name == "R");

        var methodDataMap = new Dictionary<int, byte[]>();
        var methodId = 0;

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>" || type == helperType)
                continue;

            foreach (var method in type.Methods.ToList())
            {
                if (!IsEligible(method))
                    continue;

                var serialized = SerializeMethodBody(method);
                var encrypted = XorEncrypt(serialized);
                methodDataMap[methodId] = encrypted;

                ReplaceBodyWithStub(method, module, importer, restoreMethod, methodId);

                methodId++;
                result.EncryptedMethodBodies++;
                context.Logger.Verbose($"NecroBit: {method.Name}");
            }
        }

        if (methodDataMap.Count > 0)
        {
            var resourceData = SerializeMethodDataMap(methodDataMap);
            module.Resources.Add(new EmbeddedResource(ResourceName, resourceData, ManifestResourceAttributes.Private));
        }
    }

    private static bool IsEligible(MethodDef method)
    {
        if (!method.HasBody) return false;
        if (method.IsPublic) return false;
        if (method.IsVirtual) return false;
        if (method.IsConstructor) return false;
        if (method.HasGenericParameters) return false;
        if (method.Body.ExceptionHandlers.Count > 0) return false;
        if (method.Body.Instructions.Count < 3) return false;
        if (method.Name == "Main") return false;
        return true;
    }

    private static byte[] SerializeMethodBody(MethodDef method)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var instructions = method.Body.Instructions;
        writer.Write(instructions.Count);

        foreach (var instr in instructions)
        {
            writer.Write((short)instr.OpCode.Value);

            switch (instr.OpCode.OperandType)
            {
                case OperandType.InlineNone:
                    writer.Write((byte)0);
                    break;
                case OperandType.InlineI:
                    writer.Write((byte)1);
                    writer.Write((int)instr.Operand);
                    break;
                case OperandType.InlineI8:
                    writer.Write((byte)2);
                    writer.Write((long)instr.Operand);
                    break;
                case OperandType.InlineR:
                    writer.Write((byte)3);
                    writer.Write((double)instr.Operand);
                    break;
                case OperandType.InlineString:
                    writer.Write((byte)4);
                    writer.Write((string)instr.Operand);
                    break;
                case OperandType.ShortInlineI:
                    writer.Write((byte)5);
                    if (instr.Operand is sbyte sb)
                        writer.Write(sb);
                    else if (instr.Operand is byte b)
                        writer.Write(b);
                    else
                        writer.Write((byte)0);
                    break;
                case OperandType.ShortInlineR:
                    writer.Write((byte)6);
                    writer.Write((float)instr.Operand);
                    break;
                default:
                    writer.Write((byte)255);
                    break;
            }
        }

        return ms.ToArray();
    }

    private static byte[] XorEncrypt(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ XorKey);
        return result;
    }

    private static byte[] SerializeMethodDataMap(Dictionary<int, byte[]> map)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(map.Count);
        foreach (var (id, data) in map)
        {
            writer.Write(id);
            writer.Write(data.Length);
            writer.Write(data);
        }

        return ms.ToArray();
    }

    private static void ReplaceBodyWithStub(
        MethodDef method, ModuleDef module, Importer importer,
        MethodDef restoreMethod, int methodId)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        // R(methodId, new object[] { args... }) 呼び出し
        body.Instructions.Add(Instruction.CreateLdcI4(methodId));

        // 引数配列を構築
        var paramCount = method.Parameters.Count;
        body.Instructions.Add(Instruction.CreateLdcI4(paramCount));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef));

        for (var i = 0; i < paramCount; i++)
        {
            var param = method.Parameters[i];
            body.Instructions.Add(new Instruction(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(new Instruction(OpCodes.Ldarg, param));
            if (param.Type.IsValueType)
                body.Instructions.Add(new Instruction(OpCodes.Box, param.Type.ToTypeDefOrRef()));
            body.Instructions.Add(new Instruction(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(new Instruction(OpCodes.Call, restoreMethod));

        // 戻り値を処理
        var retType = method.ReturnType;
        if (retType.FullName == "System.Void")
            body.Instructions.Add(new Instruction(OpCodes.Pop));
        else if (retType.IsValueType)
            body.Instructions.Add(new Instruction(OpCodes.Unbox_Any, retType.ToTypeDefOrRef()));
        else if (retType.FullName != "System.Object")
            body.Instructions.Add(new Instruction(OpCodes.Castclass, retType.ToTypeDefOrRef()));

        body.Instructions.Add(new Instruction(OpCodes.Ret));
    }

    private static TypeDefUser InjectNecroBitHelper(ModuleDef module, Importer importer)
    {
        var helperType = new TypeDefUser("", "\u200C\u200D\u200B", module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var objectArraySig = new SZArraySig(module.CorLibTypes.Object);
        var restoreMethod = new MethodDefUser(
            "R",
            MethodSig.CreateStatic(module.CorLibTypes.Object, module.CorLibTypes.Int32, objectArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);

        var body = new CilBody();
        restoreMethod.Body = body;
        body.InitLocals = true;

        body.Variables.Add(new Local(new SZArraySig(module.CorLibTypes.Byte))); // 0: resource data
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(Stream)))); // 1: stream
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(BinaryReader)))); // 2: reader
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 3: count
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 4: i
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 5: currentId
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 6: dataLen

        var getExecAsm = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getManifestStream = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);
        var brCtor = importer.Import(typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        var readInt32 = importer.Import(typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readBytes = importer.Import(typeof(BinaryReader).GetMethod("ReadBytes", [typeof(int)])!);

        // stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        body.Instructions.Add(new Instruction(OpCodes.Call, getExecAsm));
        body.Instructions.Add(new Instruction(OpCodes.Ldstr, ResourceName));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getManifestStream));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        // ストリームがない場合は null を返す
        var hasStream = new Instruction(OpCodes.Ldloc_1);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Brtrue, hasStream));
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        // reader = new BinaryReader(stream)
        body.Instructions.Add(hasStream);
        body.Instructions.Add(new Instruction(OpCodes.Newobj, brCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_2));

        // count = reader.ReadInt32()
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_3));

        // for (i = 0; i < count; i++)
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        var loopCheck = new Instruction(OpCodes.Ldloc, body.Variables[4]);
        var loopBody = new Instruction(OpCodes.Ldloc_2);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopCheck));

        // currentId = reader.ReadInt32()
        body.Instructions.Add(loopBody);
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[5]));

        // dataLen = reader.ReadInt32()
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[6]));

        // data = reader.ReadBytes(dataLen)
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[6]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readBytes));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_0));

        // if (currentId == methodId) → found
        var skipLabel = new Instruction(OpCodes.Ldloc, body.Variables[4]);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[5]));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, skipLabel));

        // 一致: null を返す
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        // i++
        body.Instructions.Add(skipLabel);
        body.Instructions.Add(Instruction.CreateLdcI4(1));
        body.Instructions.Add(new Instruction(OpCodes.Add));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        body.Instructions.Add(loopCheck);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Blt, loopBody));

        // not found
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        helperType.Methods.Add(restoreMethod);
        module.Types.Add(helperType);

        return helperType;
    }
}
