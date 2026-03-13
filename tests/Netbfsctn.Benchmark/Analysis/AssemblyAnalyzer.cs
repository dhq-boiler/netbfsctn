using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Netbfsctn.Benchmark.Analysis;

internal static class AssemblyAnalyzer
{
    internal static AnalysisResult Analyze(string assemblyPath)
    {
        var fileSize = new FileInfo(assemblyPath).Length;

        using var module = ModuleDefMD.Load(assemblyPath);

        var userTypes = module.Types.Where(t => t.Name != "<Module>").ToList();
        int totalTypes = userTypes.Count;
        int readableTypes = userTypes.Count(t => IsReadableName(t.Name));

        int totalMethods = 0, readableMethods = 0;
        int totalFields = 0, readableFields = 0;
        int plaintextStrings = 0;
        int totalIL = 0;
        int methodsWithBody = 0;

        foreach (var type in module.GetTypes())
        {
            foreach (var field in type.Fields)
            {
                totalFields++;
                if (IsReadableName(field.Name)) readableFields++;
            }

            foreach (var method in type.Methods)
            {
                totalMethods++;
                if (IsReadableName(method.Name)) readableMethods++;

                if (method.HasBody && method.Body.Instructions != null)
                {
                    var instructions = method.Body.Instructions;
                    totalIL += instructions.Count;
                    methodsWithBody++;

                    foreach (var instr in instructions)
                    {
                        if (instr.OpCode == OpCodes.Ldstr && instr.Operand is string)
                        {
                            plaintextStrings++;
                        }
                    }
                }
            }
        }

        double avgIL = methodsWithBody > 0 ? (double)totalIL / methodsWithBody : 0;

        bool hasAntiIldasm = module.CustomAttributes
            .Any(a => a.TypeFullName.Contains("SuppressIldasm"));

        int resourceCount = module.Resources.Count;

        return new AnalysisResult(
            totalTypes, readableTypes,
            totalMethods, readableMethods,
            totalFields, readableFields,
            plaintextStrings, totalIL, avgIL,
            hasAntiIldasm, resourceCount, fileSize);
    }

    private static bool IsReadableName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        // Compiler-generated names are not "readable" in the user-facing sense
        if (name.StartsWith('<') || name.StartsWith("CS$"))
            return false;

        // Names containing zero-width chars or confusable-only chars are obfuscated
        foreach (char c in name)
        {
            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
                return false;
        }

        // Check if name is composed only of confusable chars (l, I, O, 0, o, _)
        if (name.All(c => c is 'l' or 'I' or 'O' or '0' or 'o' or '_'))
            return false;

        return true;
    }
}
