using System.IO.Compression;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILResourceProtector : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "リソース保護 (IL)";

    private const string EncPrefix = "__enc__";
    private const byte XorKey = 0xAB;

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        // 復号ヘルパー型を注入
        var helperType = InjectResourceHelper(module);

        var resources = module.Resources.OfType<EmbeddedResource>().ToList();
        foreach (var resource in resources)
        {
            // 自分が追加したリソースや他のテクニックのリソースはスキップ
            if (resource.Name.StartsWith(EncPrefix))
                continue;

            var data = resource.GetResourceData();

            // GZip 圧縮 → XOR 暗号化
            var compressed = GZipCompress(data);
            var encrypted = XorEncrypt(compressed);

            // 元のリソースを暗号化版に置換
            module.Resources.Remove(resource);
            var encResource = new EmbeddedResource(
                EncPrefix + resource.Name,
                resource.Attributes,
                encrypted);
            module.Resources.Add(encResource);

            result.ProtectedResources++;
            context.Logger.Verbose($"リソース保護: {resource.Name}");
        }

        // リソース名マッピングテーブルを初期化子に登録
        if (result.ProtectedResources > 0)
        {
            InjectMappingInit(module, helperType, resources);
        }
    }

    private static byte[] GZipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
        {
            gz.Write(data, 0, data.Length);
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

    private static TypeDefinition InjectResourceHelper(ModuleDefinition module)
    {
        var helperType = new TypeDefinition(
            "",
            "\u200B\u200C\u200B\u200D",
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract,
            module.ImportReference(typeof(object)));

        // static byte[] Decrypt(byte[] data) メソッド
        var decryptMethod = new MethodDefinition(
            "R",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(byte[])));

        decryptMethod.Parameters.Add(new ParameterDefinition("d", ParameterAttributes.None,
            module.ImportReference(typeof(byte[]))));

        var il = decryptMethod.Body.GetILProcessor();
        decryptMethod.Body.InitLocals = true;

        // ローカル変数: byte[] xored, MemoryStream ms, GZipStream gz, MemoryStream outMs
        decryptMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(byte[]))));
        decryptMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(MemoryStream))));
        decryptMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(GZipStream))));
        decryptMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(MemoryStream))));

        // XOR 復号ループ
        // byte[] xored = new byte[d.Length];
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(byte))));
        il.Append(il.Create(OpCodes.Stloc_0));

        // for ループ用変数
        var iVar = new VariableDefinition(module.ImportReference(typeof(int)));
        decryptMethod.Body.Variables.Add(iVar); // index 4

        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc, 4));

        var loopStart = il.Create(OpCodes.Ldloc, 4);
        var loopBody = il.Create(OpCodes.Ldloc_0);
        il.Append(il.Create(OpCodes.Br, loopStart));

        // xored[i] = (byte)(d[i] ^ 0xAB)
        il.Append(loopBody);
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Ldelem_U1));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)XorKey));
        il.Append(il.Create(OpCodes.Xor));
        il.Append(il.Create(OpCodes.Conv_U1));
        il.Append(il.Create(OpCodes.Stelem_I1));

        // i++
        il.Append(il.Create(OpCodes.Ldloc, 4));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc, 4));

        // i < d.Length
        il.Append(loopStart);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Blt, loopBody));

        // GZip 解凍: MemoryStream → GZipStream → CopyTo → ToArray
        var msCtorBytes = module.ImportReference(
            typeof(MemoryStream).GetConstructor([typeof(byte[])])!);
        var msCtor = module.ImportReference(
            typeof(MemoryStream).GetConstructor(Type.EmptyTypes)!);
        var gzCtor = module.ImportReference(
            typeof(GZipStream).GetConstructor([typeof(Stream), typeof(CompressionMode)])!);
        var copyTo = module.ImportReference(
            typeof(Stream).GetMethod("CopyTo", [typeof(Stream)])!);
        var toArray = module.ImportReference(
            typeof(MemoryStream).GetMethod("ToArray")!);

        // var ms = new MemoryStream(xored);
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Newobj, msCtorBytes));
        il.Append(il.Create(OpCodes.Stloc_1));

        // var gz = new GZipStream(ms, CompressionMode.Decompress);
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldc_I4_0)); // CompressionMode.Decompress = 0
        il.Append(il.Create(OpCodes.Newobj, gzCtor));
        il.Append(il.Create(OpCodes.Stloc_2));

        // var outMs = new MemoryStream();
        il.Append(il.Create(OpCodes.Newobj, msCtor));
        il.Append(il.Create(OpCodes.Stloc_3));

        // gz.CopyTo(outMs);
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Ldloc_3));
        il.Append(il.Create(OpCodes.Callvirt, copyTo));

        // return outMs.ToArray();
        il.Append(il.Create(OpCodes.Ldloc_3));
        il.Append(il.Create(OpCodes.Callvirt, toArray));
        il.Append(il.Create(OpCodes.Ret));

        helperType.Methods.Add(decryptMethod);
        module.Types.Add(helperType);

        return helperType;
    }

    private static void InjectMappingInit(
        ModuleDefinition module,
        TypeDefinition helperType,
        List<EmbeddedResource> originalResources)
    {
        // ヘルパー型にリソース名プレフィックス定数を保持するフィールドを追加
        var prefixField = new FieldDefinition(
            "P",
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal,
            module.ImportReference(typeof(string)));
        prefixField.Constant = EncPrefix;
        helperType.Fields.Add(prefixField);
    }
}
