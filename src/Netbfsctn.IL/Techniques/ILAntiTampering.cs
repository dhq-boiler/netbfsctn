using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILAntiTampering : IObfuscationTechnique<ModuleDef>
{
    public string Name => "Anti-Tampering (IL)";

    private const string HashResourceName = "\u200B\u200D\u200C";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);

        // チェックメソッドを先に作成 (ハッシュ計算から除外するため)
        var checkMethod = CreateCheckMethod(module, importer);

        // 全メソッドの IL 命令列から簡易ハッシュを計算
        var hash = ComputeModuleHash(module, checkMethod);

        // ハッシュ値を EmbeddedResource として格納
        var hashBytes = BitConverter.GetBytes(hash);
        module.Resources.Add(new EmbeddedResource(HashResourceName, hashBytes, ManifestResourceAttributes.Private));

        // モジュール初期化子にチェックを注入
        InjectCheckInModuleCctor(module, checkMethod);

        result.AntiTamperingApplied = true;
        context.Logger.Verbose($"改ざん検出を注入しました (ハッシュ: 0x{hash:X8})");
    }

    private static uint ComputeModuleHash(ModuleDef module, MethodDef excludeMethod)
    {
        uint hash = 0;
        foreach (var type in module.Types)
        {
            foreach (var method in type.Methods)
            {
                if (method == excludeMethod) continue;
                if (!method.HasBody) continue;

                foreach (var instr in method.Body.Instructions)
                {
                    hash ^= (uint)instr.OpCode.Value;
                    hash = (hash << 7) | (hash >> 25);
                }
            }
        }
        return hash;
    }

    private static MethodDef CreateCheckMethod(ModuleDef module, Importer importer)
    {
        var moduleType = module.GlobalType;

        var checkMethod = new MethodDefUser(
            "\u200B\u200D\u200C\u200B",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Static | dnlib.DotNet.MethodAttributes.Private
                | dnlib.DotNet.MethodAttributes.HideBySig);

        var body = new CilBody();
        checkMethod.Body = body;
        body.InitLocals = true;

        // ローカル変数
        body.Variables.Add(new Local(module.CorLibTypes.UInt32)); // 0: hash
        body.Variables.Add(new Local(new SZArraySig(module.CorLibTypes.Byte))); // 1: stored
        body.Variables.Add(new Local(module.CorLibTypes.UInt32)); // 2: storedHash

        // byte[] stored = new byte[4];
        body.Instructions.Add(Instruction.CreateLdcI4(4));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        // var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("__tamper_hash__");
        var getExecAsm = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getManifestStream = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);
        var streamRead = importer.Import(
            typeof(Stream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!);
        var failFast = importer.Import(
            typeof(Environment).GetMethod("FailFast", [typeof(string)])!);
        var toUInt32 = importer.Import(
            typeof(BitConverter).GetMethod("ToUInt32", [typeof(byte[]), typeof(int)])!);

        body.Instructions.Add(new Instruction(OpCodes.Call, getExecAsm));
        body.Instructions.Add(new Instruction(OpCodes.Ldstr, HashResourceName));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getManifestStream));

        // stream.Read(stored, 0, 4);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.CreateLdcI4(4));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, streamRead));
        body.Instructions.Add(new Instruction(OpCodes.Pop));

        // storedHash = BitConverter.ToUInt32(stored, 0);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Call, toUInt32));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_2));

        // storedHash != 0 の場合のみ検証
        var exitOk = new Instruction(OpCodes.Ret);
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_2));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Conv_U4));
        body.Instructions.Add(new Instruction(OpCodes.Bne_Un, exitOk));

        // hash mismatch → Environment.FailFast
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Call, failFast));

        body.Instructions.Add(exitOk);

        moduleType.Methods.Add(checkMethod);
        return checkMethod;
    }

    private static void InjectCheckInModuleCctor(ModuleDef module, MethodDef checkMethod)
    {
        var moduleType = module.GlobalType;
        var cctor = moduleType.FindStaticConstructor();

        if (cctor == null)
        {
            cctor = new MethodDefUser(
                ".cctor",
                MethodSig.CreateStatic(module.CorLibTypes.Void),
                dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                dnlib.DotNet.MethodAttributes.Static | dnlib.DotNet.MethodAttributes.Private
                    | dnlib.DotNet.MethodAttributes.HideBySig
                    | dnlib.DotNet.MethodAttributes.SpecialName | dnlib.DotNet.MethodAttributes.RTSpecialName);
            cctor.Body = new CilBody();
            cctor.Body.Instructions.Add(new Instruction(OpCodes.Ret));
            moduleType.Methods.Add(cctor);
        }

        cctor.Body.Instructions.Insert(0, new Instruction(OpCodes.Call, checkMethod));
    }
}
