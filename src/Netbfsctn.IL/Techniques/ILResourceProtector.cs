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

        // ヘルパー型を注入: R(byte[]) -> byte[] と G(string) -> Stream
        var (helperType, decryptMethod, getStreamMethod) = InjectResourceHelper(module, importer);

        // 暗号化対象のリソースを処理
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

        // Assembly.GetManifestResourceStream(string) 呼び出しを Helper.G(string) に書き換える。
        // 暗号化リソースの利用側で「null が返るだけ」になる致命的バグの対策。
        if (result.ProtectedResources > 0)
        {
            var rewrites = RewriteResourceStreamCalls(module, helperType, getStreamMethod, importer, context);
            if (rewrites > 0)
                context.Logger.Verbose($"GetManifestResourceStream 呼び出し書き換え: {rewrites} 箇所");

            // プレフィックス定数フィールド (デバッグ・解析回避用の付帯情報)
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

    /// <summary>
    /// module 全体を走査し、`callvirt instance Stream Assembly::GetManifestResourceStream(string)` を
    /// `call Stream Helper::G(Assembly, string)` に置換する。
    /// Helper.G は第1引数の Assembly をそのまま使い、第2引数の name で
    /// 暗号化リソースの透過復号を行う。スタック上は [asm, name] のまま使えるので
    /// 余分な spill/pop が不要。
    /// </summary>
    private static int RewriteResourceStreamCalls(
        ModuleDef module, TypeDef helperType, MethodDef getStreamMethod,
        Importer importer, ObfuscationContext context)
    {
        var count = 0;
        foreach (var type in module.GetTypes())
        {
            if (type == helperType) continue;
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                var instrs = method.Body.Instructions;
                for (var i = 0; i < instrs.Count; i++)
                {
                    var instr = instrs[i];
                    if (instr.OpCode != OpCodes.Callvirt && instr.OpCode != OpCodes.Call)
                        continue;
                    if (instr.Operand is not IMethod target) continue;
                    if (target.Name != "GetManifestResourceStream") continue;
                    var declType = target.DeclaringType?.FullName;
                    if (declType != "System.Reflection.Assembly") continue;
                    var sig = target.MethodSig;
                    if (sig == null) continue;
                    // 1 引数 (string) のオーバーロードのみ対象
                    if (sig.Params.Count != 1) continue;
                    if (sig.Params[0].FullName != "System.String") continue;

                    instr.OpCode = OpCodes.Call;
                    instr.Operand = getStreamMethod;
                    count++;
                }
            }
        }
        return count;
    }

    private static (TypeDefUser type, MethodDefUser decrypt, MethodDefUser getStream) InjectResourceHelper(
        ModuleDef module, Importer importer)
    {
        var helperType = new TypeDefUser("", "​‌​‍", module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);
        var streamSig = importer.ImportAsTypeSig(typeof(Stream));

        // static byte[] R(byte[] data) - 既存
        var decryptMethod = new MethodDefUser(
            "R",
            MethodSig.CreateStatic(byteArraySig, byteArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);
        BuildDecryptBody(decryptMethod, module, importer);
        helperType.Methods.Add(decryptMethod);

        // static Stream G(Assembly asm, string name) - 新規
        // (asm は callvirt の第0引数なのでそのまま受ける)
        var asmSig = importer.ImportAsTypeSig(typeof(System.Reflection.Assembly));
        var getStreamMethod = new MethodDefUser(
            "G",
            MethodSig.CreateStatic(streamSig, asmSig, module.CorLibTypes.String),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);
        BuildGetStreamBody(getStreamMethod, module, importer, decryptMethod);
        helperType.Methods.Add(getStreamMethod);

        module.Types.Add(helperType);

        return (helperType, decryptMethod, getStreamMethod);
    }

    /// <summary>
    /// G(asm, name) の IL (asm = arg0, name = arg1):
    ///   var s = asm.GetManifestResourceStream("__enc__" + name);
    ///   if (s != null) return DecryptStream(s);
    ///   if (name.StartsWith("__enc__")) {
    ///     s = asm.GetManifestResourceStream(name);
    ///     if (s != null) return DecryptStream(s);
    ///   }
    ///   return asm.GetManifestResourceStream(name);
    /// </summary>
    private static void BuildGetStreamBody(
        MethodDefUser method, ModuleDef module, Importer importer, MethodDef decryptMethod)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        var streamSig = importer.ImportAsTypeSig(typeof(Stream));

        var localStream = new Local(streamSig);
        body.Variables.Add(localStream); // 0

        var getStream = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);
        var stringConcat = importer.Import(
            typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        var stringStartsWith = importer.Import(
            typeof(string).GetMethod("StartsWith", [typeof(string)])!);

        // s = asm.GetManifestResourceStream("__enc__" + name);
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction(EncPrefix));
        body.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getStream));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localStream));

        // if (s == null) goto checkPrefix;
        var checkPrefix = new Instruction(OpCodes.Ldarg_1);
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStream));
        body.Instructions.Add(OpCodes.Brfalse.ToInstruction(checkPrefix));

        // return DecryptStream(s);
        EmitDecryptStreamInline(body, localStream, decryptMethod, module, importer);
        body.Instructions.Add(OpCodes.Ret.ToInstruction());

        // checkPrefix: if (!name.StartsWith("__enc__")) goto fallback;
        var fallback = new Instruction(OpCodes.Ldarg_0);
        body.Instructions.Add(checkPrefix);                                // ldarg.1
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction(EncPrefix));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(stringStartsWith));
        body.Instructions.Add(OpCodes.Brfalse.ToInstruction(fallback));

        // s = asm.GetManifestResourceStream(name);
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getStream));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localStream));

        // if (s == null) goto fallback;
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStream));
        body.Instructions.Add(OpCodes.Brfalse.ToInstruction(fallback));

        // return DecryptStream(s);
        EmitDecryptStreamInline(body, localStream, decryptMethod, module, importer);
        body.Instructions.Add(OpCodes.Ret.ToInstruction());

        // fallback: return asm.GetManifestResourceStream(name);
        body.Instructions.Add(fallback);                                   // ldarg.0
        body.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getStream));
        body.Instructions.Add(OpCodes.Ret.ToInstruction());

        body.KeepOldMaxStack = true;
    }

    /// <summary>
    /// localStream の内容を全バイト読み取り、R(buf) で復号して MemoryStream として push する。
    /// 復号後の MemoryStream がスタックトップに残る。
    /// </summary>
    private static void EmitDecryptStreamInline(
        CilBody body, Local localStream, MethodDef decryptMethod,
        ModuleDef module, Importer importer)
    {
        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);

        var streamLength = importer.Import(typeof(Stream).GetProperty("Length")!.GetMethod!);
        var streamRead = importer.Import(typeof(Stream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!);
        var msCtorBytes = importer.Import(typeof(MemoryStream).GetConstructor([typeof(byte[])])!);

        var localBuf = new Local(byteArraySig);
        body.Variables.Add(localBuf);
        var localLen = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(localLen);

        // len = (int)stream.Length;
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStream));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(streamLength));
        body.Instructions.Add(OpCodes.Conv_I4.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localLen));

        // buf = new byte[len];
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localLen));
        body.Instructions.Add(OpCodes.Newarr.ToInstruction(module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localBuf));

        // stream.Read(buf, 0, len);
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStream));
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBuf));
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localLen));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(streamRead));
        body.Instructions.Add(OpCodes.Pop.ToInstruction());

        // var decrypted = R(buf);
        // return new MemoryStream(decrypted);
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBuf));
        body.Instructions.Add(OpCodes.Call.ToInstruction(decryptMethod));
        body.Instructions.Add(OpCodes.Newobj.ToInstruction(msCtorBytes));
    }

    /// <summary>
    /// 既存の R(byte[]) 復号メソッドの本体構築 (XOR 解除 → GZip 解凍 → byte[] 返却)。
    /// </summary>
    private static void BuildDecryptBody(MethodDefUser method, ModuleDef module, Importer importer)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        var byteArraySig = new SZArraySig(module.CorLibTypes.Byte);

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

        body.KeepOldMaxStack = true;
    }
}
