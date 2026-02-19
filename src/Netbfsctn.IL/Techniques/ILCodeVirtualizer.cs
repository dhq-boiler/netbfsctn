using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;
using Netbfsctn.IL.VM;

namespace Netbfsctn.IL.Techniques;

public class ILCodeVirtualizer : IObfuscationTechnique<ModuleDef>
{
    public string Name => "コード仮想化 (IL)";

    private const string BytecodeResourcePrefix = "__vm_bc_";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);
        var injector = new VmInterpreterInjector();
        var interpreterType = injector.InjectInterpreter(module);
        var executeMethod = interpreterType.Methods.First(m => m.Name == "E");

        var translator = new CilToVmTranslator(module);
        var virtualizedData = new Dictionary<int, byte[]>();
        var methodId = 0;

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>" || type == interpreterType)
                continue;

            foreach (var method in type.Methods.ToList())
            {
                if (!IsEligible(method))
                    continue;

                // NecroBit とは排他的
                if (method.Body.Instructions.Count <= 3 &&
                    method.Body.Instructions.Any(i => i.OpCode == OpCodes.Call &&
                        i.Operand is IMethod mr && mr.Name == "R"))
                    continue;

                var bytecode = translator.Translate(method);
                if (bytecode == null)
                    continue;

                virtualizedData[methodId] = bytecode;

                ReplaceWithVmCall(method, module, importer, executeMethod, methodId, translator.MetadataTokenTable);

                methodId++;
                result.VirtualizedMethods++;
                context.Logger.Verbose($"仮想化: {method.Name}");
            }
        }

        if (virtualizedData.Count > 0)
        {
            var resourceData = SerializeVmData(virtualizedData);
            module.Resources.Add(new EmbeddedResource(
                BytecodeResourcePrefix + "data", resourceData, ManifestResourceAttributes.Private));
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
        if (method.Body.Instructions.Count < 5) return false;
        if (method.Name == "Main") return false;
        return true;
    }

    private static void ReplaceWithVmCall(
        MethodDef method, ModuleDef module, Importer importer,
        MethodDef executeMethod, int methodId, List<IFullName> tokenTable)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        var getExecAsm = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getManifestStream = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);
        var brCtor = importer.Import(typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        var readInt32 = importer.Import(typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readBytes = importer.Import(typeof(BinaryReader).GetMethod("ReadBytes", [typeof(int)])!);

        body.Variables.Add(new Local(new SZArraySig(module.CorLibTypes.Byte))); // 0: bytecode
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(Stream)))); // 1: stream
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(BinaryReader)))); // 2: reader
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 3: count
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 4: i
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 5: curId
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 6: dataLen

        // stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(...)
        body.Instructions.Add(new Instruction(OpCodes.Call, getExecAsm));
        body.Instructions.Add(new Instruction(OpCodes.Ldstr, BytecodeResourcePrefix + "data"));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getManifestStream));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Newobj, brCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_2));

        // count = reader.ReadInt32()
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_3));

        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        var loopCheck = new Instruction(OpCodes.Ldloc, body.Variables[4]);
        var loopBody = new Instruction(OpCodes.Ldloc_2);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopCheck));

        // curId = reader.ReadInt32()
        body.Instructions.Add(loopBody);
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[5]));

        // dataLen = reader.ReadInt32()
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[6]));

        // bytecode = reader.ReadBytes(dataLen)
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[6]));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, readBytes));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_0));

        // if (curId == methodId) → found
        var skipLabel = new Instruction(OpCodes.Ldloc, body.Variables[4]);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[5]));
        body.Instructions.Add(Instruction.CreateLdcI4(methodId));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, skipLabel));

        // Execute(bytecode, args, tokens) 呼び出し
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));

        // args 配列
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

        // tokens 配列
        body.Instructions.Add(Instruction.CreateLdcI4(tokenTable.Count));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.String.TypeDefOrRef));
        for (var i = 0; i < tokenTable.Count; i++)
        {
            body.Instructions.Add(new Instruction(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(new Instruction(OpCodes.Ldstr, tokenTable[i].FullName));
            body.Instructions.Add(new Instruction(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(new Instruction(OpCodes.Call, executeMethod));

        // 戻り値を処理
        var retType = method.ReturnType;
        if (retType.FullName == "System.Void")
            body.Instructions.Add(new Instruction(OpCodes.Pop));
        else if (retType.IsValueType)
            body.Instructions.Add(new Instruction(OpCodes.Unbox_Any, retType.ToTypeDefOrRef()));
        else if (retType.FullName != "System.Object")
            body.Instructions.Add(new Instruction(OpCodes.Castclass, retType.ToTypeDefOrRef()));

        body.Instructions.Add(new Instruction(OpCodes.Ret));

        // i++ (skip)
        body.Instructions.Add(skipLabel);
        body.Instructions.Add(Instruction.CreateLdcI4(1));
        body.Instructions.Add(new Instruction(OpCodes.Add));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        body.Instructions.Add(loopCheck);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Blt, loopBody));

        // not found
        if (retType.FullName == "System.Void")
        {
            body.Instructions.Add(new Instruction(OpCodes.Ret));
        }
        else
        {
            body.Instructions.Add(new Instruction(OpCodes.Ldnull));
            if (retType.IsValueType)
                body.Instructions.Add(new Instruction(OpCodes.Unbox_Any, retType.ToTypeDefOrRef()));
            body.Instructions.Add(new Instruction(OpCodes.Ret));
        }
    }

    private static byte[] SerializeVmData(Dictionary<int, byte[]> data)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(data.Count);
        foreach (var (id, bytecode) in data)
        {
            writer.Write(id);
            writer.Write(bytecode.Length);
            writer.Write(bytecode);
        }
        return ms.ToArray();
    }
}
