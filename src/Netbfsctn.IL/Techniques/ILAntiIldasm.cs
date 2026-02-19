using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILAntiIldasm : IObfuscationTechnique<ModuleDef>
{
    public string Name => "Anti-ILDASM (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        // SuppressIldasmAttribute 型をモジュール内に自己定義
        var attrType = new TypeDefUser(
            "System.Runtime.CompilerServices",
            "SuppressIldasmAttribute",
            module.Import(typeof(Attribute)).ToTypeSig().ToTypeDefOrRef());
        attrType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed;

        var ctor = new MethodDefUser(
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.HideBySig
                | dnlib.DotNet.MethodAttributes.SpecialName | dnlib.DotNet.MethodAttributes.RTSpecialName);

        var body = new CilBody();
        ctor.Body = body;

        // Attribute の protected コンストラクタを呼び出し
        var importer = new Importer(module);
        var baseCtor = importer.Import(typeof(Attribute).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, Type.EmptyTypes, null)!);

        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Call, baseCtor));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        attrType.Methods.Add(ctor);
        module.Types.Add(attrType);

        // アセンブリレベルのカスタム属性として付与
        var customAttr = new CustomAttribute(ctor);
        module.Assembly.CustomAttributes.Add(customAttr);

        result.AntiIldasmApplied = true;
        context.Logger.Verbose("SuppressIldasmAttribute を付与しました");
    }
}
