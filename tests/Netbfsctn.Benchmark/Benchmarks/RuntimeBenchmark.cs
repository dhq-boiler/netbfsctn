using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Netbfsctn.Benchmark.Benchmarks;

[Orderer(SummaryOrderPolicy.Declared)]
[WarmupCount(3)]
[IterationCount(15)]
public class RuntimeBenchmark
{
    private static Dictionary<string, ScenarioPathInfo>? _scenarios;
    private static readonly string ConfigPath = Path.Combine(
        Path.GetTempPath(), "netbfsctn-benchmark-config.json");

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    private string _dllPath = "";
    private string _runtimeConfigPath = "";

    public static IEnumerable<string> ScenarioNames
    {
        get
        {
            EnsureLoaded();
            return _scenarios!.Keys;
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        EnsureLoaded();
        var info = _scenarios![Scenario];
        _dllPath = info.DllPath;
        _runtimeConfigPath = info.RuntimeConfigPath;
    }

    [Benchmark]
    public int Execute()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec --runtimeconfig \"{_runtimeConfigPath}\" \"{_dllPath}\" --verify",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return process.ExitCode;
    }

    private static void EnsureLoaded()
    {
        if (_scenarios != null) return;
        if (!File.Exists(ConfigPath))
            throw new FileNotFoundException(
                $"Benchmark config not found at {ConfigPath}. Run setup first.");
        var json = File.ReadAllText(ConfigPath);
        _scenarios = JsonSerializer.Deserialize<Dictionary<string, ScenarioPathInfo>>(json)!;
    }

    internal static void WriteConfig(Dictionary<string, ScenarioPathInfo> scenarios)
    {
        var json = JsonSerializer.Serialize(scenarios, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    internal static string GetConfigPath() => ConfigPath;
}

internal record ScenarioPathInfo(string DllPath, string RuntimeConfigPath);
