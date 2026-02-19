using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;
using Netbfsctn.IL.VM;

namespace Netbfsctn.IL.Techniques;

public class ILCodeVirtualizer : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "コード仮想化 (IL)";

    private const string BytecodeResourcePrefix = "__vm_bc_";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
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

                // NecroBit とは排他的: NecroBit で既にボディが置換されている場合はスキップ
                if (method.Body.Instructions.Count <= 3 &&
                    method.Body.Instructions.Any(i => i.OpCode == OpCodes.Call &&
                        i.Operand is MethodReference mr && mr.Name == "R"))
                    continue;

                var bytecode = translator.Translate(method);
                if (bytecode == null)
                    continue;

                virtualizedData[methodId] = bytecode;

                // メソッドボディを VM インタープリタ呼び出しスタブに置換
                ReplaceWithVmCall(method, module, executeMethod, methodId, translator.MetadataTokenTable);

                methodId++;
                result.VirtualizedMethods++;
                context.Logger.Verbose($"仮想化: {method.Name}");
            }
        }

        if (virtualizedData.Count > 0)
        {
            // VM バイトコードを EmbeddedResource に格納
            var resourceData = SerializeVmData(virtualizedData);
            module.Resources.Add(new EmbeddedResource(
                BytecodeResourcePrefix + "data",
                ManifestResourceAttributes.Private,
                resourceData));
        }
    }

    private static bool IsEligible(MethodDefinition method)
    {
        if (!method.HasBody) return false;
        if (method.IsPublic) return false;
        if (method.IsVirtual) return false;
        if (method.IsConstructor) return false;
        if (method.HasGenericParameters) return false;
        if (method.Body.HasExceptionHandlers) return false;
        if (method.Body.Instructions.Count < 5) return false;
        if (method.Name == "Main") return false;
        return true;
    }

    private static void ReplaceWithVmCall(
        MethodDefinition method,
        ModuleDefinition module,
        MethodDefinition executeMethod,
        int methodId,
        List<MemberReference> tokenTable)
    {
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();

        var il = method.Body.GetILProcessor();
        method.Body.InitLocals = true;

        // バイトコードをリソースから読み込むスタブ
        var getExecAsm = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getManifestStream = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);

        // ローカル変数
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(byte[])))); // 0: bytecode
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(Stream)))); // 1: stream

        // stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(...)
        il.Append(il.Create(OpCodes.Call, getExecAsm));
        il.Append(il.Create(OpCodes.Ldstr, BytecodeResourcePrefix + "data"));
        il.Append(il.Create(OpCodes.Callvirt, getManifestStream));
        il.Append(il.Create(OpCodes.Stloc_1));

        // BinaryReader でメソッドID に対応するバイトコードを読む
        var brCtor = module.ImportReference(typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        var readInt32 = module.ImportReference(typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readBytes = module.ImportReference(typeof(BinaryReader).GetMethod("ReadBytes", [typeof(int)])!);

        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(BinaryReader)))); // 2: reader
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 3: count

        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Newobj, brCtor));
        il.Append(il.Create(OpCodes.Stloc_2));

        // count = reader.ReadInt32()
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Stloc_3));

        // メソッドIDまでスキップ
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 4: i
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 5: curId
        method.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 6: dataLen

        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc, 4));

        var loopCheck = il.Create(OpCodes.Ldloc, 4);
        var loopBody = il.Create(OpCodes.Ldloc_2);
        il.Append(il.Create(OpCodes.Br, loopCheck));

        // curId = reader.ReadInt32()
        il.Append(loopBody);
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Stloc, 5));

        // dataLen = reader.ReadInt32()
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Stloc, 6));

        // bytecode = reader.ReadBytes(dataLen)
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Ldloc, 6));
        il.Append(il.Create(OpCodes.Callvirt, readBytes));
        il.Append(il.Create(OpCodes.Stloc_0));

        // if (curId == methodId) → found
        var skipLabel = il.Create(OpCodes.Ldloc, 4);
        il.Append(il.Create(OpCodes.Ldloc, 5));
        il.Append(il.Create(OpCodes.Ldc_I4, methodId));
        il.Append(il.Create(OpCodes.Bne_Un, skipLabel));

        // 一致: Execute(bytecode, args, tokens) 呼び出し
        // bytecode 引数
        il.Append(il.Create(OpCodes.Ldloc_0));

        // args 配列を構築
        var paramCount = method.Parameters.Count + (method.HasThis ? 1 : 0);
        il.Append(il.Create(OpCodes.Ldc_I4, paramCount));
        il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(object))));

        var argIdx = 0;
        if (method.HasThis)
        {
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4, argIdx));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            argIdx++;
        }
        foreach (var param in method.Parameters)
        {
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4, argIdx));
            il.Append(il.Create(OpCodes.Ldarg, param));
            if (param.ParameterType.IsValueType)
                il.Append(il.Create(OpCodes.Box, module.ImportReference(param.ParameterType)));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            argIdx++;
        }

        // tokens 配列 (メタデータトークンテーブルを文字列配列として格納)
        il.Append(il.Create(OpCodes.Ldc_I4, tokenTable.Count));
        il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(string))));
        for (var i = 0; i < tokenTable.Count; i++)
        {
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4, i));
            il.Append(il.Create(OpCodes.Ldstr, tokenTable[i].FullName));
            il.Append(il.Create(OpCodes.Stelem_Ref));
        }

        il.Append(il.Create(OpCodes.Call, module.ImportReference(executeMethod)));

        // 戻り値を処理
        if (method.ReturnType.FullName == "System.Void")
        {
            il.Append(il.Create(OpCodes.Pop));
        }
        else if (method.ReturnType.IsValueType)
        {
            il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(method.ReturnType)));
        }
        else if (method.ReturnType.FullName != "System.Object")
        {
            il.Append(il.Create(OpCodes.Castclass, module.ImportReference(method.ReturnType)));
        }
        il.Append(il.Create(OpCodes.Ret));

        // i++ (skip)
        il.Append(skipLabel);
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc, 4));

        il.Append(loopCheck);
        il.Append(il.Create(OpCodes.Ldloc_3));
        il.Append(il.Create(OpCodes.Blt, loopBody));

        // not found → return null/default
        if (method.ReturnType.FullName == "System.Void")
        {
            il.Append(il.Create(OpCodes.Ret));
        }
        else
        {
            il.Append(il.Create(OpCodes.Ldnull));
            if (method.ReturnType.IsValueType)
                il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(method.ReturnType)));
            il.Append(il.Create(OpCodes.Ret));
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
