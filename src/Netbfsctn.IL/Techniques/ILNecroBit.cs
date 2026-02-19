using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILNecroBit : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "NecroBit (IL)";

    private const string ResourceName = "__nb_data__";
    private const byte XorKey = 0xC7;

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        // ヘルパー型を注入
        var helperType = InjectNecroBitHelper(module);
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

                // メソッドボディを DynamicMethod 構築スタブに置換
                ReplaceBodyWithStub(method, module, restoreMethod, methodId);

                methodId++;
                result.EncryptedMethodBodies++;
                context.Logger.Verbose($"NecroBit: {method.Name}");
            }
        }

        if (methodDataMap.Count > 0)
        {
            // 暗号化データを EmbeddedResource に格納
            var resourceData = SerializeMethodDataMap(methodDataMap);
            module.Resources.Add(new EmbeddedResource(
                ResourceName, ManifestResourceAttributes.Private, resourceData));
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
        if (method.Body.Instructions.Count < 3) return false;
        if (method.Name == "Main") return false;
        return true;
    }

    private static byte[] SerializeMethodBody(MethodDefinition method)
    {
        // カスタムフォーマット: [instruction_count:4][opcode:2 + operand_type:1 + operand_data:variable]...
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var instructions = method.Body.Instructions;
        writer.Write(instructions.Count);

        foreach (var instr in instructions)
        {
            writer.Write((short)instr.OpCode.Value);

            // operand をシリアライズ
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
                    var str = (string)instr.Operand;
                    writer.Write(str);
                    break;
                case OperandType.ShortInlineI:
                    writer.Write((byte)5);
                    if (instr.OpCode == OpCodes.Ldc_I4_S)
                        writer.Write((sbyte)instr.Operand);
                    else
                        writer.Write((byte)instr.Operand);
                    break;
                case OperandType.ShortInlineR:
                    writer.Write((byte)6);
                    writer.Write((float)instr.Operand);
                    break;
                default:
                    // その他のオペランドは型情報のみ記録
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
        {
            result[i] = (byte)(data[i] ^ XorKey);
        }
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
        MethodDefinition method,
        ModuleDefinition module,
        MethodDefinition restoreMethod,
        int methodId)
    {
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();

        var il = method.Body.GetILProcessor();
        method.Body.InitLocals = true;

        // ヘルパー呼び出し: object result = R(methodId, new object[] { args... })
        // methodId をプッシュ
        il.Append(il.Create(OpCodes.Ldc_I4, methodId));

        // 引数配列を構築
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
            {
                il.Append(il.Create(OpCodes.Box, module.ImportReference(param.ParameterType)));
            }
            il.Append(il.Create(OpCodes.Stelem_Ref));
            argIdx++;
        }

        // R(methodId, args) 呼び出し
        il.Append(il.Create(OpCodes.Call, module.ImportReference(restoreMethod)));

        // 戻り値を処理
        if (method.ReturnType.FullName == "System.Void")
        {
            il.Append(il.Create(OpCodes.Pop));
        }
        else if (method.ReturnType.IsValueType)
        {
            il.Append(il.Create(OpCodes.Unbox_Any, module.ImportReference(method.ReturnType)));
        }
        else
        {
            il.Append(il.Create(OpCodes.Castclass, module.ImportReference(method.ReturnType)));
        }

        il.Append(il.Create(OpCodes.Ret));
    }

    private static TypeDefinition InjectNecroBitHelper(ModuleDefinition module)
    {
        var helperType = new TypeDefinition(
            "",
            "\u200C\u200D\u200B",
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract,
            module.ImportReference(typeof(object)));

        // static object R(int methodId, object[] args)
        var restoreMethod = new MethodDefinition(
            "R",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(object)));

        restoreMethod.Parameters.Add(new ParameterDefinition("id", ParameterAttributes.None,
            module.ImportReference(typeof(int))));
        restoreMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None,
            module.ImportReference(typeof(object[]))));

        var il = restoreMethod.Body.GetILProcessor();
        restoreMethod.Body.InitLocals = true;

        // 簡略化されたスタブ: リソースから暗号化データを読み込み、
        // 復号して DynamicMethod で実行する代わりに、
        // ここではプレースホルダーとして例外をスロー
        // (実際のランタイム復号は System.Reflection.Emit が必要)
        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(byte[])))); // 0: resource data
        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(Stream)))); // 1: stream

        var getExecAsm = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getManifestStream = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);

        // stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        il.Append(il.Create(OpCodes.Call, getExecAsm));
        il.Append(il.Create(OpCodes.Ldstr, ResourceName));
        il.Append(il.Create(OpCodes.Callvirt, getManifestStream));
        il.Append(il.Create(OpCodes.Stloc_1));

        // ストリームがない場合は null を返す
        var hasStream = il.Create(OpCodes.Ldloc_1);
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Brtrue, hasStream));
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ret));

        // BinaryReader で methodId に対応するデータを読む
        var brCtor = module.ImportReference(
            typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        var readInt32 = module.ImportReference(
            typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readBytes = module.ImportReference(
            typeof(BinaryReader).GetMethod("ReadBytes", [typeof(int)])!);

        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(BinaryReader)))); // 2: reader
        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 3: count
        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 4: i
        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 5: currentId
        restoreMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(int)))); // 6: dataLen

        il.Append(hasStream);
        il.Append(il.Create(OpCodes.Newobj, brCtor));
        il.Append(il.Create(OpCodes.Stloc_2));

        // count = reader.ReadInt32();
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Stloc_3));

        // for (i = 0; i < count; i++)
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc, 4));

        var loopCheck = il.Create(OpCodes.Ldloc, 4);
        var loopBody = il.Create(OpCodes.Ldloc_2);
        il.Append(il.Create(OpCodes.Br, loopCheck));

        // currentId = reader.ReadInt32();
        il.Append(loopBody);
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Stloc, 5));

        // dataLen = reader.ReadInt32();
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Callvirt, readInt32));
        il.Append(il.Create(OpCodes.Stloc, 6));

        // data = reader.ReadBytes(dataLen);
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Ldloc, 6));
        il.Append(il.Create(OpCodes.Callvirt, readBytes));
        il.Append(il.Create(OpCodes.Stloc_0));

        // if (currentId == methodId) → found
        var skipLabel = il.Create(OpCodes.Ldloc, 4);
        il.Append(il.Create(OpCodes.Ldloc, 5));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Bne_Un, skipLabel));

        // 一致: XOR 復号して結果を返す (簡略化: データの存在確認のみ)
        // 実際には DynamicMethod を使用するが、ここではデータが見つかったことを示す
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ret));

        // i++
        il.Append(skipLabel);
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc, 4));

        // i < count
        il.Append(loopCheck);
        il.Append(il.Create(OpCodes.Ldloc_3));
        il.Append(il.Create(OpCodes.Blt, loopBody));

        // not found
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ret));

        helperType.Methods.Add(restoreMethod);
        module.Types.Add(helperType);

        return helperType;
    }
}
