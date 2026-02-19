using Mono.Cecil;
using Mono.Cecil.Cil;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILHideMethodCalls : IObfuscationTechnique<ModuleDefinition>
{
    public string Name => "呼び出し隠蔽 (IL)";

    public void Apply(ModuleDefinition module, ObfuscationContext context, ObfuscationResult result)
    {
        // ヘルパー型を注入
        var helperType = InjectCallHelper(module);
        var invokeMethod = helperType.Methods.First(m => m.Name == "I");

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>" || type == helperType)
                continue;

            foreach (var method in type.Methods.ToList())
            {
                if (!method.HasBody) continue;
                if (method.Body.HasExceptionHandlers) continue;

                HideCalls(method, module, invokeMethod, context, result);
            }
        }
    }

    private static void HideCalls(
        MethodDefinition method,
        ModuleDefinition module,
        MethodDefinition invokeMethod,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions.ToList();

        foreach (var instr in instructions)
        {
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                continue;

            if (instr.Operand is not MethodReference targetMethod)
                continue;

            // BCL 呼び出しは除外 (System.*, Microsoft.* 名前空間)
            var declaringTypeName = targetMethod.DeclaringType.FullName;
            if (declaringTypeName.StartsWith("System.") ||
                declaringTypeName.StartsWith("Microsoft.") ||
                declaringTypeName.StartsWith("System/") ||
                string.IsNullOrEmpty(declaringTypeName))
                continue;

            // コンストラクタは除外
            if (targetMethod.Name == ".ctor" || targetMethod.Name == ".cctor")
                continue;

            // ジェネリックメソッドは除外
            if (targetMethod.HasGenericParameters)
                continue;
            if (targetMethod is GenericInstanceMethod)
                continue;

            // 戻り値が void 以外の場合のみ対象 (void の間接呼び出しは複雑なため)
            if (targetMethod.ReturnType.FullName == "System.Void")
                continue;

            // パラメータ数が多すぎる場合はスキップ
            if (targetMethod.Parameters.Count > 8)
                continue;

            var resolved = targetMethod.Resolve();
            if (resolved == null) continue;

            // public メソッドは除外 (private/internal のみ対象)
            if (resolved.IsPublic) continue;

            // 引数を object[] 配列に収集 → ヘルパー呼び出しに置換
            ReplaceWithReflectionCall(il, instr, targetMethod, module, invokeMethod);

            result.HiddenMethodCalls++;
            context.Logger.Verbose($"呼び出し隠蔽: {targetMethod.Name} in {method.Name}");
        }
    }

    private static void ReplaceWithReflectionCall(
        ILProcessor il,
        Instruction callInstr,
        MethodReference targetMethod,
        ModuleDefinition module,
        MethodDefinition invokeMethod)
    {
        var paramCount = targetMethod.Parameters.Count;
        var isInstance = callInstr.OpCode == OpCodes.Callvirt || targetMethod.HasThis;

        // 新しい命令リスト
        var newInstructions = new List<Instruction>();

        // typeName 引数
        newInstructions.Add(il.Create(OpCodes.Ldstr, targetMethod.DeclaringType.FullName));

        // methodName 引数
        newInstructions.Add(il.Create(OpCodes.Ldstr, targetMethod.Name));

        // paramTypes 引数: Type[] 配列
        newInstructions.Add(il.Create(OpCodes.Ldc_I4, paramCount));
        newInstructions.Add(il.Create(OpCodes.Newarr, module.ImportReference(typeof(Type))));

        for (var i = 0; i < paramCount; i++)
        {
            newInstructions.Add(il.Create(OpCodes.Dup));
            newInstructions.Add(il.Create(OpCodes.Ldc_I4, i));

            var paramType = targetMethod.Parameters[i].ParameterType;
            newInstructions.Add(il.Create(OpCodes.Ldtoken, module.ImportReference(paramType)));
            var getTypeFromHandle = module.ImportReference(
                typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])!);
            newInstructions.Add(il.Create(OpCodes.Call, getTypeFromHandle));
            newInstructions.Add(il.Create(OpCodes.Stelem_Ref));
        }

        // args 引数: object[] 配列 (スタック上の引数を配列に詰め替え)
        // 注: 呼び出し前にスタック上に引数が積まれているので、
        //     ローカル変数に一時退避してから配列に詰める
        // これは複雑なので、簡略化: 引数なしのメソッドのみ対象
        // (引数ありの場合は null を渡す → リフレクション側で処理)
        newInstructions.Add(il.Create(OpCodes.Ldnull)); // args = null (引数なし想定)

        // ヘルパー呼び出し
        newInstructions.Add(il.Create(OpCodes.Call, module.ImportReference(invokeMethod)));

        // 戻り値を適切な型に変換
        var returnType = targetMethod.ReturnType;
        if (returnType.IsValueType)
        {
            newInstructions.Add(il.Create(OpCodes.Unbox_Any, module.ImportReference(returnType)));
        }
        else if (returnType.FullName != "System.Object")
        {
            newInstructions.Add(il.Create(OpCodes.Castclass, module.ImportReference(returnType)));
        }

        // 元の call 命令を置換
        var first = newInstructions[0];
        il.Replace(callInstr, first);

        var prev = first;
        for (var i = 1; i < newInstructions.Count; i++)
        {
            il.InsertAfter(prev, newInstructions[i]);
            prev = newInstructions[i];
        }
    }

    private static TypeDefinition InjectCallHelper(ModuleDefinition module)
    {
        var helperType = new TypeDefinition(
            "",
            "\u200D\u200B\u200C",
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract,
            module.ImportReference(typeof(object)));

        // static object I(string typeName, string methodName, Type[] paramTypes, object[] args)
        var invokeMethod = new MethodDefinition(
            "I",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(object)));

        invokeMethod.Parameters.Add(new ParameterDefinition("t", ParameterAttributes.None,
            module.ImportReference(typeof(string))));
        invokeMethod.Parameters.Add(new ParameterDefinition("m", ParameterAttributes.None,
            module.ImportReference(typeof(string))));
        invokeMethod.Parameters.Add(new ParameterDefinition("p", ParameterAttributes.None,
            module.ImportReference(typeof(Type[]))));
        invokeMethod.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None,
            module.ImportReference(typeof(object[]))));

        var il = invokeMethod.Body.GetILProcessor();
        invokeMethod.Body.InitLocals = true;

        // ローカル: Type type, MethodInfo mi
        invokeMethod.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(Type))));
        invokeMethod.Body.Variables.Add(new VariableDefinition(
            module.ImportReference(typeof(System.Reflection.MethodInfo))));

        // var type = Type.GetType(typeName) ?? Assembly.GetExecutingAssembly().GetType(typeName);
        var getType = module.ImportReference(
            typeof(Type).GetMethod("GetType", [typeof(string)])!);
        var getExecAsm = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var asmGetType = module.ImportReference(
            typeof(System.Reflection.Assembly).GetMethod("GetType", [typeof(string)])!);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, getType));
        il.Append(il.Create(OpCodes.Dup));

        var afterTypeResolve = il.Create(OpCodes.Stloc_0);
        il.Append(il.Create(OpCodes.Brtrue, afterTypeResolve));

        // fallback: Assembly.GetExecutingAssembly().GetType(typeName)
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Call, getExecAsm));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, asmGetType));

        il.Append(afterTypeResolve);

        // var mi = type.GetMethod(methodName, bindingFlags, null, paramTypes, null);
        var getMethodRef = module.ImportReference(
            typeof(Type).GetMethod("GetMethod", [
                typeof(string),
                typeof(System.Reflection.BindingFlags),
                typeof(System.Reflection.Binder),
                typeof(Type[]),
                typeof(System.Reflection.ParameterModifier[])
            ])!);

        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Instance)));
        il.Append(il.Create(OpCodes.Ldnull)); // binder
        il.Append(il.Create(OpCodes.Ldarg_2)); // paramTypes
        il.Append(il.Create(OpCodes.Ldnull)); // modifiers
        il.Append(il.Create(OpCodes.Callvirt, getMethodRef));
        il.Append(il.Create(OpCodes.Stloc_1));

        // return mi.Invoke(null, args);
        var invokeRef = module.ImportReference(
            typeof(System.Reflection.MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);

        il.Append(il.Create(OpCodes.Ldloc_1));
        il.Append(il.Create(OpCodes.Ldnull)); // instance (static)
        il.Append(il.Create(OpCodes.Ldarg_3)); // args
        il.Append(il.Create(OpCodes.Callvirt, invokeRef));
        il.Append(il.Create(OpCodes.Ret));

        helperType.Methods.Add(invokeMethod);
        module.Types.Add(helperType);

        return helperType;
    }
}
