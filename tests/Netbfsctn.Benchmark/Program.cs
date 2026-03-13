using System.Diagnostics;
using Netbfsctn.Benchmark.Analysis;
using Netbfsctn.Benchmark.Config;
using Netbfsctn.Benchmark.Reporting;
using Netbfsctn.Benchmark.Runners;

var repoRoot = FindRepoRoot();
var workDir = Path.Combine(repoRoot, "tests", "Netbfsctn.Benchmark", "bin", "benchmark-work");
Directory.CreateDirectory(workDir);

Console.WriteLine("=== Netbfsctn Benchmark ===");
Console.WriteLine();

// Step 1: Build projects
Console.WriteLine("[1/5] Building projects...");
var buildCli = ProcessRunner.Run("dotnet", $"build \"{Path.Combine(repoRoot, "src", "Netbfsctn.Cli")}\" -c Release -v q --nologo");
if (buildCli.ExitCode != 0) { Console.Error.WriteLine("Failed to build CLI:\n" + buildCli.StdErr); return 1; }

var buildSample = ProcessRunner.Run("dotnet", $"publish \"{Path.Combine(repoRoot, "tests", "Netbfsctn.Benchmark.SampleApp")}\" -c Release -o \"{Path.Combine(workDir, "original")}\" -v q --nologo");
if (buildSample.ExitCode != 0) { Console.Error.WriteLine("Failed to build SampleApp:\n" + buildSample.StdErr); return 1; }

Console.WriteLine("  Build OK.");

// Step 2: Find executables
var netbfsctnExe = Path.Combine(repoRoot, "src", "Netbfsctn.Cli", "bin", "Release", "net10.0", "netbfsctn.exe");
if (!File.Exists(netbfsctnExe))
    netbfsctnExe = Path.Combine(repoRoot, "src", "Netbfsctn.Cli", "bin", "Release", "net10.0", "netbfsctn");
if (!File.Exists(netbfsctnExe)) { Console.Error.WriteLine("Cannot find netbfsctn executable"); return 1; }

var originalDll = Path.Combine(workDir, "original", "Netbfsctn.Benchmark.SampleApp.dll");
var originalRuntimeConfig = Path.Combine(workDir, "original", "Netbfsctn.Benchmark.SampleApp.runtimeconfig.json");
if (!File.Exists(originalDll)) { Console.Error.WriteLine($"Cannot find SampleApp DLL at {originalDll}"); return 1; }

Console.WriteLine($"  CLI: {netbfsctnExe}");
Console.WriteLine($"  SampleApp: {originalDll}");

// Step 3: Baseline analysis
Console.WriteLine();
Console.WriteLine("[2/5] Analyzing baseline...");
var baselineAnalysis = AssemblyAnalyzer.Analyze(originalDll);
var baselineChecksum = GetChecksum(originalDll, originalRuntimeConfig);
var baselineRuntime = MeasureRuntime(originalDll, originalRuntimeConfig);
Console.WriteLine($"  Baseline checksum: {baselineChecksum}");
Console.WriteLine($"  Baseline runtime: {baselineRuntime:F1}ms");

var allResults = new List<BenchmarkResult>
{
    new("Baseline", true, null, baselineAnalysis, true, baselineRuntime, 0, 0)
};

// Step 4: Run scenarios
Console.WriteLine();
Console.WriteLine($"[3/5] Running {ScenarioDefinitions.All.Length} scenarios...");
int scenarioIdx = 0;
foreach (var scenario in ScenarioDefinitions.All)
{
    scenarioIdx++;
    Console.Write($"  [{scenarioIdx}/{ScenarioDefinitions.All.Length}] {scenario.Name}...");

    var scenarioDir = Path.Combine(workDir, $"scenario_{scenarioIdx:D2}");
    Directory.CreateDirectory(scenarioDir);

    // Copy original files to scenario dir
    foreach (var file in Directory.GetFiles(Path.Combine(workDir, "original")))
        File.Copy(file, Path.Combine(scenarioDir, Path.GetFileName(file)), true);

    var outputDll = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.obfuscated.dll");
    var runtimeConfig = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.runtimeconfig.json");

    // Run obfuscation
    var inputDll = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.dll");
    var obfResult = ProcessRunner.Run(netbfsctnExe, $"\"{inputDll}\" -o \"{outputDll}\" {scenario.CliOptions}");

    if (obfResult.ExitCode != 0)
    {
        Console.WriteLine(" FAILED");
        var errorMsg = obfResult.StdErr.Length > 0 ? obfResult.StdErr.Trim() : obfResult.StdOut.Trim();
        allResults.Add(new BenchmarkResult(scenario.Name, false, errorMsg, null, false, 0, 0, 0));
        continue;
    }

    // Copy runtimeconfig for the obfuscated dll
    var obfRuntimeConfig = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.obfuscated.runtimeconfig.json");
    if (File.Exists(runtimeConfig))
        File.Copy(runtimeConfig, obfRuntimeConfig, true);

    // Analyze
    var analysis = AssemblyAnalyzer.Analyze(outputDll);

    // Correctness check
    var checksum = GetChecksum(outputDll, obfRuntimeConfig);
    bool correct = checksum == baselineChecksum;

    // Runtime measurement
    double runtime = 0;
    double runtimeDelta = 0;
    if (correct)
    {
        runtime = MeasureRuntime(outputDll, obfRuntimeConfig);
        runtimeDelta = baselineRuntime > 0 ? (runtime - baselineRuntime) / baselineRuntime * 100 : 0;
    }

    double sizeDelta = (double)(analysis.FileSizeBytes - baselineAnalysis.FileSizeBytes) / baselineAnalysis.FileSizeBytes * 100;

    allResults.Add(new BenchmarkResult(scenario.Name, true, null, analysis, correct, runtime, runtimeDelta, sizeDelta));
    Console.WriteLine($" OK (size: {sizeDelta:+0.0;-0.0}%, correct: {(correct ? "yes" : "NO")})");
}

// Step 5: Report
Console.WriteLine();
Console.WriteLine("[4/5] Generating report...");
ReportGenerator.PrintConsole(baselineAnalysis, allResults);

var markdownPath = Path.Combine(repoRoot, "benchmark-results.md");
ReportGenerator.WriteMarkdown(markdownPath, baselineAnalysis, allResults);
Console.WriteLine($"[5/5] Markdown report saved to: {markdownPath}");

return 0;

// --- Helper methods ---

string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "netbfsctn.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    // Fallback: try working directory
    dir = Directory.GetCurrentDirectory();
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "netbfsctn.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    throw new InvalidOperationException("Cannot find repository root (netbfsctn.slnx)");
}

string? GetChecksum(string dllPath, string runtimeConfigPath)
{
    try
    {
        var result = ProcessRunner.Run("dotnet", $"exec --runtimeconfig \"{runtimeConfigPath}\" \"{dllPath}\" --verify", timeoutMs: 30000);
        if (result.ExitCode != 0) return null;
        var line = result.StdOut.Split('\n').FirstOrDefault(l => l.StartsWith("CHECKSUM:"));
        return line?.Trim();
    }
    catch
    {
        return null;
    }
}

double MeasureRuntime(string dllPath, string runtimeConfigPath, int iterations = 5)
{
    var times = new List<double>();
    for (int i = 0; i < iterations; i++)
    {
        try
        {
            var result = ProcessRunner.Run("dotnet", $"exec --runtimeconfig \"{runtimeConfigPath}\" \"{dllPath}\" --verify", timeoutMs: 30000);
            if (result.ExitCode == 0)
                times.Add(result.Elapsed.TotalMilliseconds);
        }
        catch { /* skip failed iterations */ }
    }
    return times.Count > 0 ? times.Average() : 0;
}
