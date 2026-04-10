using Netbfsctn.Benchmark.Analysis;

namespace Netbfsctn.Benchmark.Reporting;

internal static class ReportGenerator
{
    internal static void PrintConsole(AnalysisResult baseline, List<BenchmarkResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════╦══════════╦═════════╦══════════════════╦═════════╦═══════════╦═══════════╦════════════╦══════════╗");
        Console.WriteLine("║ Scenario             ║ Size     ║ Size Δ% ║ Mean ± StdDev    ║ Time Δ% ║ Names R/T ║ Strings   ║ AvgIL/Meth ║ Correct? ║");
        Console.WriteLine("╠══════════════════════╬══════════╬═════════╬══════════════════╬═════════╬═══════════╬═══════════╬════════════╬══════════╣");

        var b = results[0];
        PrintRow("Baseline", baseline.FileSizeBytes, 0, b.MeanRuntimeMs, b.StdDevMs, 0,
            $"{baseline.ReadableTypeNames + baseline.ReadableMethodNames + baseline.ReadableFieldNames}/{baseline.TotalTypes + baseline.TotalMethods + baseline.TotalFields}",
            baseline.PlaintextStringCount.ToString(), baseline.AvgILInstructionsPerMethod, true);

        foreach (var r in results.Skip(1))
        {
            if (!r.ObfuscationSuccess)
            {
                PrintFailedRow(r.ScenarioName);
                continue;
            }

            var a = r.Analysis!;
            var readableNames = a.ReadableTypeNames + a.ReadableMethodNames + a.ReadableFieldNames;
            var totalNames = a.TotalTypes + a.TotalMethods + a.TotalFields;

            PrintRow(r.ScenarioName, a.FileSizeBytes, r.SizeDeltaPercent, r.MeanRuntimeMs, r.StdDevMs, r.RuntimeDeltaPercent,
                $"{readableNames}/{totalNames}", a.PlaintextStringCount.ToString(),
                a.AvgILInstructionsPerMethod, r.CorrectnessPass);
        }

        Console.WriteLine("╚══════════════════════╩══════════╩═════════╩══════════════════╩═════════╩═══════════╩═══════════╩════════════╩══════════╝");
        Console.WriteLine();
    }

    private static void PrintRow(string name, long size, double sizeDelta, double meanMs, double stdDevMs, double timeDelta,
        string names, string strings, double avgIL, bool correct)
    {
        var timeStr = $"{meanMs:F1} ± {stdDevMs:F1}ms";
        Console.WriteLine($"║ {name,-20} ║ {FormatSize(size),8} ║ {FormatDelta(sizeDelta),7} ║ {timeStr,16} ║ {FormatDelta(timeDelta),7} ║ {names,9} ║ {strings,9} ║ {avgIL,10:F1} ║ {(correct ? "OK" : "FAIL"),8} ║");
    }

    private static void PrintFailedRow(string name)
    {
        Console.WriteLine($"║ {name,-20} ║ {"FAILED",8} ║ {"---",7} ║ {"---",16} ║ {"---",7} ║ {"---",9} ║ {"---",9} ║ {"---",10} ║ {"---",8} ║");
    }

    private static string FormatSize(long bytes)
    {
        return bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024):F1} MB"
            : $"{bytes / 1024.0:F1} KB";
    }

    private static string FormatDelta(double delta)
    {
        if (delta == 0) return "-";
        return $"{delta:+0.0;-0.0}%";
    }

    internal static void WriteMarkdown(string path, AnalysisResult baseline, List<BenchmarkResult> results)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("# Netbfsctn Benchmark Results");
        sw.WriteLine();
        sw.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sw.WriteLine();
        sw.WriteLine("Powered by [BenchmarkDotNet](https://benchmarkdotnet.org/)");
        sw.WriteLine();

        // Summary table
        sw.WriteLine("## Summary");
        sw.WriteLine();
        sw.WriteLine("| Scenario | Size | Size Δ% | Mean (ms) | StdDev (ms) | Median (ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |");
        sw.WriteLine("|----------|------|---------|-----------|-------------|-------------|---------|----------------|-------------------|---------------|---------|");

        WriteMarkdownRow(sw, "Baseline", baseline.FileSizeBytes, 0, results[0], baseline, true);

        foreach (var r in results.Skip(1))
        {
            if (!r.ObfuscationSuccess)
            {
                sw.WriteLine($"| {r.ScenarioName} | FAILED | - | - | - | - | - | - | - | - | - |");
                continue;
            }
            WriteMarkdownRow(sw, r.ScenarioName, r.Analysis!.FileSizeBytes, r.SizeDeltaPercent, r, r.Analysis, r.CorrectnessPass);
        }

        // Detail sections
        sw.WriteLine();
        sw.WriteLine("## Baseline Assembly Details");
        sw.WriteLine();
        sw.WriteLine($"- Types: {baseline.TotalTypes} (readable: {baseline.ReadableTypeNames})");
        sw.WriteLine($"- Methods: {baseline.TotalMethods} (readable: {baseline.ReadableMethodNames})");
        sw.WriteLine($"- Fields: {baseline.TotalFields} (readable: {baseline.ReadableFieldNames})");
        sw.WriteLine($"- Plaintext strings: {baseline.PlaintextStringCount}");
        sw.WriteLine($"- Total IL instructions: {baseline.TotalILInstructions}");
        sw.WriteLine($"- Resources: {baseline.ResourceCount}");

        foreach (var r in results.Skip(1).Where(r => r.ObfuscationSuccess))
        {
            var a = r.Analysis!;
            sw.WriteLine();
            sw.WriteLine($"## {r.ScenarioName}");
            sw.WriteLine();
            sw.WriteLine($"- Types: {a.TotalTypes} (readable: {a.ReadableTypeNames})");
            sw.WriteLine($"- Methods: {a.TotalMethods} (readable: {a.ReadableMethodNames})");
            sw.WriteLine($"- Fields: {a.TotalFields} (readable: {a.ReadableFieldNames})");
            sw.WriteLine($"- Plaintext strings: {a.PlaintextStringCount}");
            sw.WriteLine($"- Total IL instructions: {a.TotalILInstructions}");
            sw.WriteLine($"- Avg IL/method: {a.AvgILInstructionsPerMethod:F1}");
            sw.WriteLine($"- Has SuppressIldasm: {a.HasSuppressIldasmAttribute}");
            sw.WriteLine($"- Resources: {a.ResourceCount}");
            sw.WriteLine($"- Size: {FormatSize(a.FileSizeBytes)} ({r.SizeDeltaPercent:+0.0;-0.0}%)");
            sw.WriteLine($"- Runtime: {r.MeanRuntimeMs:F1} ± {r.StdDevMs:F1}ms (median: {r.MedianRuntimeMs:F1}ms, {r.RuntimeDeltaPercent:+0.0;-0.0}%)");
            sw.WriteLine($"- Correct: {(r.CorrectnessPass ? "Yes" : "No")}");
        }
    }

    private static void WriteMarkdownRow(StreamWriter sw, string name, long size, double sizeDelta,
        BenchmarkResult r, AnalysisResult a, bool correct)
    {
        var readable = a.ReadableTypeNames + a.ReadableMethodNames + a.ReadableFieldNames;
        var total = a.TotalTypes + a.TotalMethods + a.TotalFields;
        sw.WriteLine($"| {name} | {FormatSize(size)} | {FormatDelta(sizeDelta)} | {r.MeanRuntimeMs:F1} | {r.StdDevMs:F1} | {r.MedianRuntimeMs:F1} | {FormatDelta(r.RuntimeDeltaPercent)} | {readable}/{total} | {a.PlaintextStringCount} | {a.AvgILInstructionsPerMethod:F1} | {(correct ? "OK" : "FAIL")} |");
    }
}
