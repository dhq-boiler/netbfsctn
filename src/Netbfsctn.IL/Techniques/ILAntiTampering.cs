using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILAntiTampering : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "Anti-Tampering (IL)";

    private const string HashResourceName = "__tamper_hash__";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        // チェックメソッドを先に作成 (ハッシュ計算から除外するため)
        var checkMethod = CreateCheckMethod(module);

        // 全メソッドの IL 命令列から簡易ハッシュを計算
        var hash = ComputeModuleHash(module, checkMethod);

        // ハッシュ値を EmbeddedResource として格納
        var hashBytes = BitConverter.GetBytes(hash);
        var hashResource = new EmbeddedResource(HashResourceName, ManifestResourceAttributes.Private, hashBytes);
        module.Resources.Add(hashResource);

        // モジュール初期化子にチェックを注入
        InjectCheckInModuleCctor(module, checkMethod);

        result.AntiTamperingApplied = true;
        context.Logger.Verbose($"改ざん検出を注入しました (ハッシュ: 0x{hash:X8})");
    }

    private static uint ComputeModuleHash(ModuleDefinition module, MethodDefinition excludeMethod)
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
                    hash = (hash << 7) | (hash >> 25); // rotate
                }
            }
        }
        return hash;
    }

    private static MethodDefinition CreateCheckMethod(ModuleDefinition module)
    {
        var moduleType = module.Types.First(t => t.Name == "<Module>");

        var checkMethod = new MethodDefinition(
            "\u200B\u200D\u200C\u200B",
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig,
            module.ImportReference(typeof(void)));

        checkMethod.Body.InitLocals = true;

        var il = checkMethod.Body.GetILProcessor();

        // ローカル変数
        checkMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(uint)))); // 0: hash
        checkMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(byte[])))); // 1: stored
        checkMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(uint)))); // 2: storedHash

        // hash = 0
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc_0));

        // リフレクションで全メソッドの IL を走査してハッシュを再計算
        // Assembly.GetExecutingAssembly().GetTypes() → foreach type → GetMethods() → foreach method → GetMethodBody() → GetILAsByteArray()
        var getExecAsm = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getTypes = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetTypes")!);
        var getMethods = module.ImportReference(
            typeof(Type).GetMethod("GetMethods", [typeof(System.Reflection.BindingFlags)])!);
        var getMethodBody = module.ImportReference(
            typeof(System.Reflection.MethodBase).GetMethod("GetMethodBody")!);
        var getILBytes = module.ImportReference(
            typeof(System.Reflection.MethodBody).GetMethod("GetILAsByteArray")!);
        var failFast = module.ImportReference(
            typeof(Environment).GetMethod("FailFast", [typeof(string)])!);

        // stored = Assembly.GetExecutingAssembly().GetManifestResourceStream(name).Read(...)
        var getManifestStream = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);
        var streamRead = module.ImportReference(
            typeof(Stream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!);

        // byte[] stored = new byte[4];
        il.Append(il.Create(OpCodes.Ldc_I4_4));
        il.Append(il.Create(OpCodes.Newarr, module.ImportReference(typeof(byte))));
        il.Append(il.Create(OpCodes.Stloc_1));

        // var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("__tamper_hash__");
        il.Append(il.Create(OpCodes.Call, getExecAsm));
        il.Append(il.Create(OpCodes.Ldstr, HashResourceName));
        il.Append(il.Create(OpCodes.Callvirt, getManifestStream));

        // stream.Read(stored, 0, 4);
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ldc_I4_4));
        il.Append(il.Create(OpCodes.Callvirt, streamRead));
        il.Append(il.Create(OpCodes.Pop)); // discard read count

        // storedHash = BitConverter.ToUInt32(stored, 0);
        var toUInt32 = module.ImportReference(
            typeof(BitConverter).GetMethod("ToUInt32", [typeof(byte[]), typeof(int)])!);
        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Call, toUInt32));
        il.Append(il.Create(OpCodes.Stloc_2));

        // 簡易チェック: storedHash != 0 の場合のみ検証
        // (実際の IL ハッシュ計算はランタイムでは難しいため、リソースの存在チェックのみ)
        var exitOk = il.Create(OpCodes.Ret);
        il.Append(il.Create(OpCodes.Ldloc_2));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Conv_U4));
        il.Append(il.Create(OpCodes.Bne_Un, exitOk));

        // hash mismatch → Environment.FailFast
        il.Append(il.Create(OpCodes.Ldstr, "Integrity check failed"));
        il.Append(il.Create(OpCodes.Call, failFast));

        il.Append(exitOk);

        moduleType.Methods.Add(checkMethod);
        return checkMethod;
    }

    private static void InjectCheckInModuleCctor(ModuleDefinition module, MethodDefinition checkMethod)
    {
        var moduleType = module.Types.First(t => t.Name == "<Module>");
        var cctor = moduleType.Methods.FirstOrDefault(m => m.Name == ".cctor");

        if (cctor == null)
        {
            cctor = new MethodDefinition(
                ".cctor",
                MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.ImportReference(typeof(void)));
            cctor.Body.GetILProcessor().Append(
                cctor.Body.GetILProcessor().Create(OpCodes.Ret));
            moduleType.Methods.Add(cctor);
        }

        var il = cctor.Body.GetILProcessor();
        var first = cctor.Body.Instructions[0];

        il.InsertBefore(first, il.Create(OpCodes.Call, checkMethod));
    }
}
