using Mono.Cecil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILAntiIldasm : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "Anti-ILDASM (IL)";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        // SuppressIldasmAttribute 型をモジュール内に自己定義
        var attrType = new TypeDefinition(
            "System.Runtime.CompilerServices",
            "SuppressIldasmAttribute",
            TypeAttributes.NotPublic | TypeAttributes.Sealed,
            module.ImportReference(typeof(Attribute)));

        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            module.ImportReference(typeof(void)));

        var il = ctor.Body.GetILProcessor();
        // Attribute の protected コンストラクタを取得
        var baseCtor = module.ImportReference(
            typeof(Attribute).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, Type.EmptyTypes, null)!);
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Call, baseCtor));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));

        attrType.Methods.Add(ctor);
        module.Types.Add(attrType);

        // アセンブリレベルのカスタム属性として付与
        var customAttr = new CustomAttribute(ctor);
        module.Assembly.CustomAttributes.Add(customAttr);

        result.AntiIldasmApplied = true;
        context.Logger.Verbose("SuppressIldasmAttribute を付与しました");
    }
}
