using System.IO.Compression;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILResourceProtector : IObfuscationTechnique<ModuleDef>
{
    public string Name => "リソース保護 (IL)";

    private const string EncPrefix = "__enc__";
    private const byte XorKey = 0xAB;

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);

        // 復号ヘルパー型を注入
        var helperType = InjectResourceHelper(module, importer);

        var resources = module.Resources.OfType<EmbeddedResource>().ToList();
        foreach (var resource in resources)
        {
            if (resource.Name.String.StartsWith(EncPrefix))
                continue;

            if (ShouldSkipResource(resource.Name.String))
            {
                context.Logger.Verbose($"リソース保護スキップ (WPF/BAML): {resource.Name}");
                continue;
            }

            var data = resource.CreateReader().ToArray();

            var compressed = GZipCompress(data);
            var encrypted = XorEncrypt(compressed);

            module.Resources.Remove(resource);
            module.Resources.Add(new EmbeddedResource(
                EncPrefix + resource.Name.String, encrypted, ManifestResourceAttributes.Private));

            result.ProtectedResources++;
            context.Logger.Verbose($"リソース保護: {resource.Name}");
        }

        if (result.ProtectedResources > 0)
        {
            // プレフィックス定数フィールドを追加
            var prefixField = new FieldDefUser(
                "P",
                new FieldSig(module.CorLibTypes.String),
                dnlib.DotNet.FieldAttributes.Public | dnlib.DotNet.FieldAttributes.Static
                    | dnlib.DotNet.FieldAttributes.Literal);
            prefixField.Constant = module.UpdateRowId(new ConstantUser(EncPrefix));
            helperType.Fields.Add(prefixField);
        }
    }

    private static bool ShouldSkipResource(string name)
    {
        return name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] GZipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
            gz.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] XorEncrypt(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ XorKey);
        return result;
    }

    private static TypeDefUser InjectResourceHelper(ModuleDef module, Importer importer)
    {
        var helperType = new TypeDefUser("", "\u200B\u200C\u200B\u200D", module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);

        // static byte[] R(byte[] data)
        var decryptMethod = new MethodDefUser(
            "R",
            MethodSig.CreateStatic(byteArraySig, byteArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);

        var body = new CilBody();
        decryptMethod.Body = body;
        body.InitLocals = true;

        body.Variables.Add(new Local(byteArraySig)); // 0: xored
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(MemoryStream)))); // 1: ms
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(GZipStream)))); // 2: gz
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(MemoryStream)))); // 3: outMs
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // 4: i

        // byte[] xored = new byte[d.Length];
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldlen));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I4));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_0));

        // i = 0
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        var loopStart = new Instruction(OpCodes.Ldloc, body.Variables[4]);
        var loopBody = new Instruction(OpCodes.Ldloc_0);
        body.Instructions.Add(new Instruction(OpCodes.Br, loopStart));

        // xored[i] = (byte)(d[i] ^ 0xAB)
        body.Instructions.Add(loopBody);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(new Instruction(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.CreateLdcI4(XorKey));
        body.Instructions.Add(new Instruction(OpCodes.Xor));
        body.Instructions.Add(new Instruction(OpCodes.Conv_U1));
        body.Instructions.Add(new Instruction(OpCodes.Stelem_I1));

        // i++
        body.Instructions.Add(new Instruction(OpCodes.Ldloc, body.Variables[4]));
        body.Instructions.Add(Instruction.CreateLdcI4(1));
        body.Instructions.Add(new Instruction(OpCodes.Add));
        body.Instructions.Add(new Instruction(OpCodes.Stloc, body.Variables[4]));

        // i < d.Length
        body.Instructions.Add(loopStart);
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldlen));
        body.Instructions.Add(new Instruction(OpCodes.Conv_I4));
        body.Instructions.Add(new Instruction(OpCodes.Blt, loopBody));

        // GZip 解凍
        var msCtorBytes = importer.Import(typeof(MemoryStream).GetConstructor([typeof(byte[])])!);
        var msCtor = importer.Import(typeof(MemoryStream).GetConstructor(Type.EmptyTypes)!);
        var gzCtor = importer.Import(
            typeof(GZipStream).GetConstructor([typeof(Stream), typeof(CompressionMode)])!);
        var copyTo = importer.Import(typeof(Stream).GetMethod("CopyTo", [typeof(Stream)])!);
        var toArray = importer.Import(typeof(MemoryStream).GetMethod("ToArray")!);

        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Newobj, msCtorBytes));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(Instruction.CreateLdcI4(0)); // CompressionMode.Decompress
        body.Instructions.Add(new Instruction(OpCodes.Newobj, gzCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_2));

        body.Instructions.Add(new Instruction(OpCodes.Newobj, msCtor));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_3));

        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, copyTo));

        body.Instructions.Add(new Instruction(OpCodes.Ldloc_3));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, toArray));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        helperType.Methods.Add(decryptMethod);
        module.Types.Add(helperType);

        return helperType;
    }
}
