using dnlib.DotNet;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILNameObfuscator : IObfuscationTechnique<ModuleDef>
{
    public string Name => "名前難読化 (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;

            ProcessType(type, context, result);
        }
    }

    private void ProcessType(TypeDef type, ObfuscationContext context, ObfuscationResult result)
    {
        var options = context.Options;

        // ネストされた型を先に処理
        foreach (var nested in type.NestedTypes)
        {
            ProcessType(nested, context, result);
        }

        // public な型はスキップ
        if (type.IsPublic || type.IsNestedPublic)
            return;

        // コンパイラ生成型はスキップ（匿名型、クロージャ、ステートマシン等）
        if (IsCompilerGenerated(type))
            return;

        // フィールドの名前変更
        if (options.EnableRenameFields)
        {
            foreach (var field in type.Fields)
            {
                if (field.IsPublic)
                    continue;

                var newName = context.NameGenerator.Next();
                context.NameMap[field.FullName] = newName;
                context.Logger.Verbose($"フィールド: {field.Name} -> {newName}");
                field.Name = newName;
                result.RenamedSymbols++;
            }
        }

        // メソッドの名前変更
        if (options.EnableRenameMethods)
        {
            foreach (var method in type.Methods)
            {
                if (ShouldSkipMethod(method))
                    continue;

                var newName = context.NameGenerator.Next();
                context.NameMap[method.FullName] = newName;
                context.Logger.Verbose($"メソッド: {method.Name} -> {newName}");
                method.Name = newName;
                result.RenamedSymbols++;
            }
        }

        // プロパティの名前変更（getter/setterメソッド名も連動）
        if (options.EnableRenameProperties)
        {
            foreach (var property in type.Properties)
            {
                if (property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true)
                    continue;

                var newName = context.NameGenerator.Next();
                context.Logger.Verbose($"プロパティ: {property.Name} -> {newName}");
                property.Name = newName;
                result.RenamedSymbols++;

                if (property.GetMethod != null && !property.GetMethod.IsPublic)
                {
                    var getterName = "get_" + newName;
                    context.NameMap[property.GetMethod.FullName] = getterName;
                    property.GetMethod.Name = getterName;
                }
                if (property.SetMethod != null && !property.SetMethod.IsPublic)
                {
                    var setterName = "set_" + newName;
                    context.NameMap[property.SetMethod.FullName] = setterName;
                    property.SetMethod.Name = setterName;
                }
            }
        }

        // 型自体の名前変更
        if (options.EnableRenameTypes)
        {
            var newTypeName = context.NameGenerator.Next();
            context.NameMap[type.FullName] = newTypeName;
            context.Logger.Verbose($"型: {type.Name} -> {newTypeName}");
            type.Name = newTypeName;
            result.RenamedSymbols++;
        }
    }

    private static bool ShouldSkipMethod(MethodDef method)
    {
        if (method.IsPublic) return true;
        if (method.IsConstructor) return true;
        if (method.Name == "Main") return true;
        if (method.IsVirtual) return true;
        if (method.HasOverrides) return true;
        if (method.IsSpecialName) return true;
        return false;
    }

    private static bool IsCompilerGenerated(TypeDef type)
    {
        var name = type.Name.String;
        // 匿名型: <>f__AnonymousType
        if (name.StartsWith("<>f__AnonymousType"))
            return true;
        // クロージャ: <>c__DisplayClass
        if (name.StartsWith("<>c__DisplayClass") || name == "<>c")
            return true;
        // async ステートマシン: <MethodName>d__
        if (name.Contains(">d__"))
            return true;
        // イテレータ ステートマシン
        if (name.Contains(">e__"))
            return true;
        // CompilerGeneratedAttribute
        if (type.CustomAttributes.Any(a => a.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;
        return false;
    }
}
