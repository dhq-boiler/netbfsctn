using BenchmarkDotNet.Running;
using Netbfsctn.Benchmark.Analysis;
using Netbfsctn.Benchmark.Benchmarks;
using Netbfsctn.Benchmark.Config;
using Netbfsctn.Benchmark.Reporting;
using Netbfsctn.Benchmark.Runners;

// BenchmarkDotNet spawns child processes with special args — detect and skip setup
if (args.Length > 0 && args.Any(a => a.StartsWith("--benchmarkDotNet") || a.StartsWith("--cli")))
{
    BenchmarkSwitcher.FromAssembly(typeof(RuntimeBenchmark).Assembly).Run(args);
    return 0;
}

var repoRoot = FindRepoRoot();
var workDir = Path.Combine(repoRoot, "tests", "Netbfsctn.Benchmark", "bin", "benchmark-work");
Directory.CreateDirectory(workDir);

Console.WriteLine("=== Netbfsctn Benchmark (BenchmarkDotNet) ===");
Console.WriteLine();

// Step 1: Build projects
Console.WriteLine("[1/6] Building projects...");
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
Console.WriteLine("[2/6] Analyzing baseline...");
var baselineAnalysis = AssemblyAnalyzer.Analyze(originalDll);
var baselineChecksum = GetChecksum(originalDll, originalRuntimeConfig);
Console.WriteLine($"  Baseline checksum: {baselineChecksum}");

// Step 4: Run obfuscation for all scenarios and collect analysis
Console.WriteLine();
Console.WriteLine($"[3/6] Running {ScenarioDefinitions.All.Length} obfuscation scenarios...");

var scenarioPaths = new Dictionary<string, ScenarioPathInfo>();
var scenarioData = new Dictionary<string, (AnalysisResult? Analysis, bool Correct, double SizeDelta)>();

// Add baseline
scenarioPaths["Baseline"] = new ScenarioPathInfo(originalDll, originalRuntimeConfig);
scenarioData["Baseline"] = (baselineAnalysis, true, 0);

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
        scenarioData[scenario.Name] = (null, false, 0);
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

    double sizeDelta = (double)(analysis.FileSizeBytes - baselineAnalysis.FileSizeBytes) / baselineAnalysis.FileSizeBytes * 100;

    scenarioPaths[scenario.Name] = new ScenarioPathInfo(outputDll, obfRuntimeConfig);
    scenarioData[scenario.Name] = (analysis, correct, sizeDelta);

    Console.WriteLine($" OK (size: {sizeDelta:+0.0;-0.0}%, correct: {(correct ? "yes" : "NO")})");
}

// Step 5: Write config and run BenchmarkDotNet
Console.WriteLine();
Console.WriteLine("[4/6] Running BenchmarkDotNet...");
RuntimeBenchmark.WriteConfig(scenarioPaths);
Console.WriteLine($"  Config: {RuntimeBenchmark.GetConfigPath()}");

var summary = BenchmarkRunner.Run<RuntimeBenchmark>();

// Step 6: Merge results and generate report
Console.WriteLine();
Console.WriteLine("[5/6] Merging results...");

var allResults = new List<BenchmarkResult>();

// Extract BenchmarkDotNet results per scenario
var bdnResults = new Dictionary<string, (double Mean, double Median, double StdDev)>();
foreach (var report in summary.Reports)
{
    var scenarioName = report.BenchmarkCase.Parameters["Scenario"]?.ToString() ?? "";
    if (report.ResultStatistics != null)
    {
        var stats = report.ResultStatistics;
        // BenchmarkDotNet reports in nanoseconds by default
        bdnResults[scenarioName] = (
            Mean: stats.Mean / 1_000_000,
            Median: stats.Median / 1_000_000,
            StdDev: stats.StandardDeviation / 1_000_000
        );
    }
}

// Build baseline result
var baselineBdn = bdnResults.GetValueOrDefault("Baseline");
double baselineMean = baselineBdn.Mean;

allResults.Add(new BenchmarkResult("Baseline", true, null, baselineAnalysis, true,
    baselineBdn.Mean, baselineBdn.Median, baselineBdn.StdDev, 0, 0));

foreach (var scenario in ScenarioDefinitions.All)
{
    var (analysis, correct, sizeDelta) = scenarioData.GetValueOrDefault(scenario.Name);
    if (analysis == null)
    {
        allResults.Add(new BenchmarkResult(scenario.Name, false, "Obfuscation failed", null, false, 0, 0, 0, 0, 0));
        continue;
    }

    var bdn = bdnResults.GetValueOrDefault(scenario.Name);
    double runtimeDelta = baselineMean > 0 ? (bdn.Mean - baselineMean) / baselineMean * 100 : 0;

    allResults.Add(new BenchmarkResult(scenario.Name, true, null, analysis, correct,
        bdn.Mean, bdn.Median, bdn.StdDev, runtimeDelta, sizeDelta));
}

// Generate report
ReportGenerator.PrintConsole(baselineAnalysis, allResults);

var markdownPath = Path.Combine(repoRoot, "benchmark-results.md");
ReportGenerator.WriteMarkdown(markdownPath, baselineAnalysis, allResults);
Console.WriteLine($"[6/6] Markdown report saved to: {markdownPath}");

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
