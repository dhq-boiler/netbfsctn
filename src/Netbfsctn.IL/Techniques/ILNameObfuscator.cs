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
        // ネストされた型を先に処理
        foreach (var nested in type.NestedTypes)
        {
            ProcessType(nested, context, result);
        }

        // public な型はスキップ
        if (type.IsPublic || type.IsNestedPublic)
            return;

        // フィールドの名前変更
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

        // メソッドの名前変更
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

        // プロパティの名前変更
        foreach (var property in type.Properties)
        {
            if (property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true)
                continue;

            var newName = context.NameGenerator.Next();
            context.Logger.Verbose($"プロパティ: {property.Name} -> {newName}");
            property.Name = newName;
            result.RenamedSymbols++;
        }

        // 型自体の名前変更
        var newTypeName = context.NameGenerator.Next();
        context.NameMap[type.FullName] = newTypeName;
        context.Logger.Verbose($"型: {type.Name} -> {newTypeName}");
        type.Name = newTypeName;
        result.RenamedSymbols++;
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
}
