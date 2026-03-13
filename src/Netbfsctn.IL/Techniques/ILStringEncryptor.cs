using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Encryption;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILStringEncryptor : IObfuscationTechnique<ModuleDef>
{
    public string Name => "文字列暗号化 (IL)";

    private const string HelperTypeName = "\u200B\u200C\u200D";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var encryptor = new XorStringEncryptor();
        var importer = new Importer(module);

        // 既存のヘルパー型があれば再利用、なければ注入
        var existingHelper = module.Types.FirstOrDefault(t => t.Name == HelperTypeName);
        var helperType = existingHelper ?? InjectDecryptionHelper(module, importer);
        var decryptMethod = helperType.Methods.First(m => m.Name == "D");

        foreach (var type in module.GetTypes())
        {
            if (type == helperType)
                continue;

            // 他のヘルパー型（ゼロ幅文字名）をスキップ
            if (IsInjectedHelperType(type.Name))
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;

                EncryptStringsInMethod(method, module, importer, decryptMethod, encryptor, context, result);
            }
        }
    }

    private void EncryptStringsInMethod(
        MethodDef method,
        ModuleDef module,
        Importer importer,
        MethodDef decryptMethod,
        XorStringEncryptor encryptor,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        var body = method.Body;
        var instructions = body.Instructions.ToList();

        foreach (var instr in instructions)
        {
            if (instr.OpCode != OpCodes.Ldstr)
                continue;

            var original = (string)instr.Operand;
            if (original == null)
                continue;

            var key = encryptor.GenerateKey();
            var encrypted = encryptor.Encrypt(original, key);

            var newInstructions = new List<Instruction>();

            // 暗号化データの配列を生成
            newInstructions.AddRange(CreateByteArrayLoadInstructions(encrypted, module));
            newInstructions.AddRange(CreateByteArrayLoadInstructions(key, module));
            newInstructions.Add(new Instruction(OpCodes.Call, decryptMethod));

            // 元の ldstr をインプレースで書き換え（分岐ターゲット参照を保持）
            var idx = body.Instructions.IndexOf(instr);
            instr.OpCode = newInstructions[0].OpCode;
            instr.Operand = newInstructions[0].Operand;

            // 残りの命令を後続に挿入
            for (var i = 1; i < newInstructions.Count; i++)
            {
                body.Instructions.Insert(idx + i, newInstructions[i]);
            }

            result.EncryptedStrings++;
            context.Logger.Verbose($"暗号化: \"{Truncate(original, 30)}\" in {method.Name}");
        }
    }

    private static List<Instruction> CreateByteArrayLoadInstructions(byte[] data, ModuleDef module)
    {
        var instructions = new List<Instruction>();

        // 配列サイズ
        instructions.Add(Instruction.CreateLdcI4(data.Length));
        instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));

        for (var i = 0; i < data.Length; i++)
        {
            instructions.Add(new Instruction(OpCodes.Dup));
            instructions.Add(Instruction.CreateLdcI4(i));
            instructions.Add(Instruction.CreateLdcI4(data[i]));
            instructions.Add(new Instruction(OpCodes.Stelem_I1));
        }

        return instructions;
    }

    private TypeDefUser InjectDecryptionHelper(ModuleDef module, Importer importer)
    {
        var helperType = new TypeDefUser(
            "",
            "\u200B\u200C\u200D",
            module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);

        var decryptMethod = new MethodDefUser(
            "D",
            MethodSig.CreateStatic(module.CorLibTypes.String, byteArraySig, byteArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);

        decryptMethod.ParamDefs.Add(new ParamDefUser("d", 1));
        decryptMethod.ParamDefs.Add(new ParamDefUser("k", 2));

        var body = new CilBody();
        decryptMethod.Body = body;
        body.InitLocals = true;

        // byte[] result = new byte[d.Length];
        var resultVar = new Local(byteArraySig);
        var iVar = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(resultVar);
        body.Variables.Add(iVar);

        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldlen));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I4));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_0));

        // i = 0
        body.Instructions.Add(new Instruction(OpCodes.Ldc_I4_0));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        // ループ先頭
        var loopStart = new Instruction(OpCodes.Ldloc_1);
        var loopBody = new Instruction(OpCodes.Ldloc_0);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // result[i] = (byte)(d[i] ^ k[i % k.Length])
        body.Instructions.Add(loopBody);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));

        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_U1));

        body.Instructions.Add(new Instruction(OpCodes.Ldarg_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldlen));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I4));
        body.Instructions.Add(new Instruction(OpCodes.Rem));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_U1));

        body.Instructions.Add(new Instruction(OpCodes.Xor));
        body.Instructions.Add(new Instruction(OpCodes.Conv_U1));
        body.Instructions.Add(new Instruction(OpCodes.Stelem_I1));

        // i++
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldc_I4_1));
        body.Instructions.Add(new Instruction(OpCodes.Add));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        // i < d.Length
        body.Instructions.Add(loopStart);
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldlen));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I4));
        body.Instructions.Add(new Instruction(OpCodes.Blt, loopBody));

        // return Encoding.UTF8.GetString(result)
        var getUtf8 = importer.Import(typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        var getString = importer.Import(typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[])])!);

        body.Instructions.Add(new Instruction(OpCodes.Call, getUtf8));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getString));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        helperType.Methods.Add(decryptMethod);
        module.Types.Add(helperType);

        return helperType;
    }

    private static bool IsInjectedHelperType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF');
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";
}
