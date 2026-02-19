using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILHideMethodCalls : IObfuscationTechnique<ModuleDef>
{
    public string Name => "呼び出し隠蔽 (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);

        // ヘルパー型を注入
        var helperType = InjectCallHelper(module, importer);
        var invokeMethod = helperType.Methods.First(m => m.Name == "I");

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>" || type == helperType)
                continue;

            foreach (var method in type.Methods.ToList())
            {
                if (!method.HasBody) continue;
                if (method.Body.ExceptionHandlers.Count > 0) continue;

                HideCalls(method, module, importer, invokeMethod, context, result);
            }
        }
    }

    private static void HideCalls(
        MethodDef method, ModuleDef module, Importer importer,
        MethodDef invokeMethod, ObfuscationContext context, ObfuscationResult result)
    {
        var body = method.Body;
        var instructions = body.Instructions.ToList();

        foreach (var instr in instructions)
        {
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                continue;

            if (instr.Operand is not IMethod targetMethod)
                continue;

            var declaringTypeName = targetMethod.DeclaringType?.FullName ?? "";
            if (declaringTypeName.StartsWith("System.") || declaringTypeName.StartsWith("Microsoft.")
                || string.IsNullOrEmpty(declaringTypeName))
                continue;

            if (targetMethod.Name == ".ctor" || targetMethod.Name == ".cctor")
                continue;

            var methodSig = targetMethod.MethodSig;
            if (methodSig == null) continue;
            if (methodSig.GenParamCount > 0) continue;
            if (methodSig.RetType.FullName == "System.Void") continue;
            if (methodSig.Params.Count > 8) continue;

            var resolved = (targetMethod as MethodDef) ?? (targetMethod as MemberRef)?.ResolveMethod();
            if (resolved == null) continue;
            if (resolved.IsPublic) continue;

            ReplaceWithReflectionCall(body, instr, targetMethod, module, importer, invokeMethod);

            result.HiddenMethodCalls++;
            context.Logger.Verbose($"呼び出し隠蔽: {targetMethod.Name} in {method.Name}");
        }
    }

    private static void ReplaceWithReflectionCall(
        CilBody body, Instruction callInstr, IMethod targetMethod,
        ModuleDef module, Importer importer, MethodDef invokeMethod)
    {
        var methodSig = targetMethod.MethodSig;
        var paramCount = methodSig!.Params.Count;

        var newInstructions = new List<Instruction>();

        // typeName
        newInstructions.Add(new Instruction(OpCodes.Ldstr, targetMethod.DeclaringType.FullName));
        // methodName
        newInstructions.Add(new Instruction(OpCodes.Ldstr, targetMethod.Name.String));

        // paramTypes: Type[] 配列
        var typeTypeRef = importer.Import(typeof(Type));
        var getTypeFromHandle = importer.Import(typeof(Type).GetMethod("GetTypeFromHandle",
            [typeof(RuntimeTypeHandle)])!);

        newInstructions.Add(Instruction.CreateLdcI4(paramCount));
        newInstructions.Add(new Instruction(OpCodes.Newarr, typeTypeRef));

        for (var i = 0; i < paramCount; i++)
        {
            newInstructions.Add(new Instruction(OpCodes.Dup));
            newInstructions.Add(Instruction.CreateLdcI4(i));
            var paramType = methodSig.Params[i].ToTypeDefOrRef();
            newInstructions.Add(new Instruction(OpCodes.Ldtoken, paramType));
            newInstructions.Add(new Instruction(OpCodes.Call, getTypeFromHandle));
            newInstructions.Add(new Instruction(OpCodes.Stelem_Ref));
        }

        // args = null
        newInstructions.Add(new Instruction(OpCodes.Ldnull));

        // ヘルパー呼び出し
        newInstructions.Add(new Instruction(OpCodes.Call, invokeMethod));

        // 戻り値を適切な型に変換
        var returnType = methodSig.RetType;
        if (returnType.IsValueType)
            newInstructions.Add(new Instruction(OpCodes.Unbox_Any, returnType.ToTypeDefOrRef()));
        else if (returnType.FullName != "System.Object")
            newInstructions.Add(new Instruction(OpCodes.Castclass, returnType.ToTypeDefOrRef()));

        // 元の call 命令を置換
        var idx = body.Instructions.IndexOf(callInstr);
        body.Instructions[idx] = newInstructions[0];

        // 分岐ターゲットの更新
        foreach (var instr in body.Instructions)
        {
            if (instr.Operand == callInstr)
                instr.Operand = newInstructions[0];
            if (instr.Operand is Instruction[] targets)
                for (var i = 0; i < targets.Length; i++)
                    if (targets[i] == callInstr)
                        targets[i] = newInstructions[0];
        }

        for (var i = 1; i < newInstructions.Count; i++)
            body.Instructions.Insert(idx + i, newInstructions[i]);
    }

    private static TypeDefUser InjectCallHelper(ModuleDef module, Importer importer)
    {
        var helperType = new TypeDefUser("", "\u200D\u200B\u200C", module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var typeArraySig = new SZArraySig(importer.ImportAsTypeSig(typeof(Type)));
        var objectArraySig = new SZArraySig(module.CorLibTypes.Object);

        var invokeMethod = new MethodDefUser(
            "I",
            MethodSig.CreateStatic(module.CorLibTypes.Object,
                module.CorLibTypes.String, module.CorLibTypes.String, typeArraySig, objectArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);

        var body = new CilBody();
        invokeMethod.Body = body;
        body.InitLocals = true;

        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(Type)))); // 0: type
        body.Variables.Add(new Local(importer.ImportAsTypeSig(typeof(System.Reflection.MethodInfo)))); // 1: mi

        var getType = importer.Import(typeof(Type).GetMethod("GetType", [typeof(string)])!);
        var getExecAsm = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var asmGetType = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetType", [typeof(string)])!);
        var getMethodRef = importer.Import(
            typeof(Type).GetMethod("GetMethod", [
                typeof(string), typeof(System.Reflection.BindingFlags),
                typeof(System.Reflection.Binder), typeof(Type[]),
                typeof(System.Reflection.ParameterModifier[])
            ])!);
        var invokeRef = importer.Import(
            typeof(System.Reflection.MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);

        // var type = Type.GetType(typeName)
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Call, getType));
        body.Instructions.Add(new Instruction(OpCodes.Dup));

        var afterTypeResolve = new Instruction(OpCodes.Stloc_0);
        body.Instructions.Add(new Instruction(OpCodes.Brtrue, afterTypeResolve));

        // fallback
        body.Instructions.Add(new Instruction(OpCodes.Pop));
        body.Instructions.Add(new Instruction(OpCodes.Call, getExecAsm));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_0));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, asmGetType));

        body.Instructions.Add(afterTypeResolve);

        // mi = type.GetMethod(...)
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_0));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.CreateLdcI4((int)(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Instance)));
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_2));
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, getMethodRef));
        body.Instructions.Add(new Instruction(OpCodes.Stloc_1));

        // return mi.Invoke(null, args)
        body.Instructions.Add(new Instruction(OpCodes.Ldloc_1));
        body.Instructions.Add(new Instruction(OpCodes.Ldnull));
        body.Instructions.Add(new Instruction(OpCodes.Ldarg_3));
        body.Instructions.Add(new Instruction(OpCodes.Callvirt, invokeRef));
        body.Instructions.Add(new Instruction(OpCodes.Ret));

        helperType.Methods.Add(invokeMethod);
        module.Types.Add(helperType);

        return helperType;
    }
}
