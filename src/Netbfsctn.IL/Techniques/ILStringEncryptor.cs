using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Encryption;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILStringEncryptor : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "文字列暗号化 (IL)";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        var encryptor = new XorStringEncryptor();

        // 復号ヘルパー型を注入
        var helperType = InjectDecryptionHelper(module);
        var decryptMethod = helperType.Methods.First(m => m.Name == "D");

        foreach (var type in module.Types)
        {
            if (type == helperType || type.Name == "<Module>")
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;

                EncryptStringsInMethod(method, module, decryptMethod, encryptor, context, result);
            }
        }
    }

    private void EncryptStringsInMethod(
        MethodDefinition method,
        ModuleDefinition module,
        MethodDefinition decryptMethod,
        XorStringEncryptor encryptor,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions.ToList();

        foreach (var instr in instructions)
        {
            if (instr.OpCode != OpCodes.Ldstr)
                continue;

            var original = (string)instr.Operand;
            if (string.IsNullOrEmpty(original))
                continue;

            var key = encryptor.GenerateKey();
            var encrypted = encryptor.Encrypt(original, key);

            // ldstr を以下に置換:
            // 1. 暗号化バイト配列をロード
            // 2. キーバイト配列をロード
            // 3. D(byte[], byte[]) を呼び出し

            var newInstructions = new List<Instruction>();

            // 暗号化データの配列を生成
            newInstructions.AddRange(CreateByteArrayLoadInstructions(il, encrypted, module));
            newInstructions.AddRange(CreateByteArrayLoadInstructions(il, key, module));
            newInstructions.Add(il.Create(OpCodes.Call, module.ImportReference(decryptMethod)));

            // 元の ldstr を最初の命令に置き換え
            var first = newInstructions[0];
            il.Replace(instr, first);

            // 残りの命令を後続に挿入
            var prev = first;
            for (var i = 1; i < newInstructions.Count; i++)
            {
                il.InsertAfter(prev, newInstructions[i]);
                prev = newInstructions[i];
            }

            result.EncryptedStrings++;
            context.Logger.Verbose($"暗号化: \"{Truncate(original, 30)}\" in {method.Name}");
        }
    }

    private static List<Instruction> CreateByteArrayLoadInstructions(
        ILProcessor il, byte[] data, ModuleDefinition module)
    {
        var instructions = new List<Instruction>();

        // 配列サイズ
        instructions.Add(il.Create(OpCodes.Ldc_I4, data.Length));
        instructions.Add(il.Create(OpCodes.Newarr, module.ImportReference(typeof(byte))));

        for (var i = 0; i < data.Length; i++)
        {
            instructions.Add(il.Create(OpCodes.Dup));
            instructions.Add(il.Create(OpCodes.Ldc_I4, i));
            instructions.Add(il.Create(OpCodes.Ldc_I4, (int)data[i]));
            instructions.Add(il.Create(OpCodes.Stelem_I1));
        }

        return instructions;
    }

    private TypeDefinition InjectDecryptionHelper(ModuleDefinition module)
    {
        var helperType = new TypeDefinition(
            "",
            "\u200B\u200C\u200D",
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract,
            module.ImportReference(typeof(object)));

        var decryptMethod = new MethodDefinition(
            "D",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(string)));

        decryptMethod.Parameters.Add(new ParameterDefinition("d", ParameterAttributes.None,
            module.ImportReference(typeof(byte[]))));
        decryptMethod.Parameters.Add(new ParameterDefinition("k", ParameterAttributes.None,
            module.ImportReference(typeof(byte[]))));

        var il = decryptMethod.Body.GetILProcessor();
        decryptMethod.Body.InitLocals = true;

        // byte[] result = new byte[d.Length];
        var resultVar = new VariableDefinition(module.ImportReference(typeof(byte[])));
        var iVar = new VariableDefinition(module.ImportReference(typeof(int)));
        decryptMethod.Body.Variables.Add(resultVar);
        decryptMethod.Body.Variables.Add(iVar);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(byte))));
        il.Append(il.Create(OpCodes.Stloc_0));

        // i = 0
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc_1));

        // ループ先頭
        var loopStart = il.Create(OpCodes.Ldloc_1);
        var loopBody = il.Create(OpCodes.Ldloc_0);
        il.Append(il.Create(OpCodes.Br, loopStart));

        // result[i] = (byte)(d[i] ^ k[i % k.Length])
        il.Append(loopBody);
        il.Append(il.Create(OpCodes.Ldloc_1));

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldelem_U1));

        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Rem));
        il.Append(il.Create(OpCodes.Ldelem_U1));

        il.Append(il.Create(OpCodes.Xor));
        il.Append(il.Create(OpCodes.Conv_U1));
        il.Append(il.Create(OpCodes.Stelem_I1));

        // i++
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc_1));

        // i < d.Length
        il.Append(loopStart);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Blt, loopBody));

        // return Encoding.UTF8.GetString(result)
        var getUtf8 = module.ImportReference(
            typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        var getString = module.ImportReference(
            typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[])])!);

        il.Append(il.Create(OpCodes.Call, getUtf8));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Callvirt, getString));
        il.Append(il.Create(OpCodes.Ret));

        helperType.Methods.Add(decryptMethod);
        module.Types.Add(helperType);

        return helperType;
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";
}
