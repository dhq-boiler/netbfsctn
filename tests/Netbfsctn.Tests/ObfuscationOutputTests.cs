using System.Diagnostics;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL;

namespace Netbfsctn.Tests;

/// <summary>
/// 難読化前後でプログラムの出力が一致するかを検証する統合テスト。
/// SampleApp を各テクニック構成で難読化し、 --verify モードのチェックサムが
/// 難読化前と一致することで、難読化が動作を破壊していないことを確認する。
/// </summary>
public class ObfuscationOutputTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sampleAppDir;
    private readonly string _sampleDll;
    private readonly string _runtimeConfig;
    private readonly string _baselineChecksum;
    private readonly bool _sampleAvailable;

    public ObfuscationOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "netbfsctn_output_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _sampleAppDir = Path.Combine(_tempDir, "original");
        var repoRoot = FindRepoRoot();
        var sampleProj = Path.Combine(repoRoot, "tests", "Netbfsctn.Benchmark.SampleApp",
            "Netbfsctn.Benchmark.SampleApp.csproj");

        if (File.Exists(sampleProj))
        {
            var publishResult = RunProcess("dotnet",
                $"publish \"{sampleProj}\" -c Release -o \"{_sampleAppDir}\" -v q --nologo");
            _sampleAvailable = publishResult.ExitCode == 0;
        }

        _sampleDll = Path.Combine(_sampleAppDir, "Netbfsctn.Benchmark.SampleApp.dll");
        _runtimeConfig = Path.Combine(_sampleAppDir, "Netbfsctn.Benchmark.SampleApp.runtimeconfig.json");

        // ベースラインチェックサムを取得
        _baselineChecksum = "";
        if (_sampleAvailable)
        {
            var result = RunDotnetExec(_sampleDll, _runtimeConfig, "--verify");
            if (result.ExitCode == 0)
                _baselineChecksum = ExtractChecksum(result.StdOut);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Baseline_Checksum_IsNotEmpty()
    {
        if (!_sampleAvailable) return;
        Assert.False(string.IsNullOrEmpty(_baselineChecksum), "ベースラインチェックサムが取得できませんでした");
    }

    [Fact]
    public void Output_DefaultOptions_MatchesBaseline()
    {
        if (!_sampleAvailable) return;
        AssertOutputMatchesBaseline(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
        }, "default");
    }

    [Fact]
    public void Output_RenamePublic_MatchesBaseline()
    {
        if (!_sampleAvailable) return;
        AssertOutputMatchesBaseline(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableRenamePublic = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
        }, "rename_public");
    }

    [Fact]
    public void Output_FullPipeline_MatchesBaseline()
    {
        if (!_sampleAvailable) return;
        AssertOutputMatchesBaseline(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
            EnableAntiDebug = true,
            EnableAntiTampering = true,
            EnableResourceProtection = true,
        }, "full_pipeline");
    }

    [Fact]
    public void Output_FullPipelineWithRenamePublic_MatchesBaseline()
    {
        if (!_sampleAvailable) return;
        AssertOutputMatchesBaseline(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableRenamePublic = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
            EnableAntiDebug = true,
            EnableAntiTampering = true,
            EnableResourceProtection = true,
        }, "full_rename_public");
    }

    [Fact]
    public void Output_NoRenameRenameFieldsRenamePublic_MatchesBaseline()
    {
        if (!_sampleAvailable) return;
        AssertOutputMatchesBaseline(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableRenameTypes = false,
            EnableRenameFields = true,
            EnableRenameMethods = false,
            EnableRenameProperties = false,
            EnableRenamePublic = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
            EnableResourceProtection = true,
            EnableAntiDebug = true,
            EnableAntiTampering = true,
        }, "vara_style");
    }

    // --- Helper methods ---

    private void AssertOutputMatchesBaseline(ObfuscationOptions baseOptions, string scenarioName)
    {
        Assert.False(string.IsNullOrEmpty(_baselineChecksum), "ベースラインチェックサムがありません");

        var scenarioDir = Path.Combine(_tempDir, scenarioName);
        Directory.CreateDirectory(scenarioDir);

        foreach (var file in Directory.GetFiles(_sampleAppDir))
            File.Copy(file, Path.Combine(scenarioDir, Path.GetFileName(file)), true);

        var inputDll = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.dll");
        var outputDll = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.obfuscated.dll");

        var options = new ObfuscationOptions
        {
            InputPath = inputDll,
            OutputPath = outputDll,
            EnableRename = baseOptions.EnableRename,
            EnableRenameTypes = baseOptions.EnableRenameTypes,
            EnableRenameFields = baseOptions.EnableRenameFields,
            EnableRenameMethods = baseOptions.EnableRenameMethods,
            EnableRenameProperties = baseOptions.EnableRenameProperties,
            EnableRenamePublic = baseOptions.EnableRenamePublic,
            EnableStringEncryption = baseOptions.EnableStringEncryption,
            EnableControlFlow = baseOptions.EnableControlFlow,
            EnableDeadCode = baseOptions.EnableDeadCode,
            EnableAntiDebug = baseOptions.EnableAntiDebug,
            EnableAntiTampering = baseOptions.EnableAntiTampering,
            EnableResourceProtection = baseOptions.EnableResourceProtection,
            EnableAntiIldasm = baseOptions.EnableAntiIldasm,
            EnableNecroBit = baseOptions.EnableNecroBit,
            EnableHideMethodCalls = baseOptions.EnableHideMethodCalls,
            EnableCodeVirtualization = baseOptions.EnableCodeVirtualization,
            Verbose = true,
        };

        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };

        var pipeline = new ILObfuscationPipeline();
        var result = pipeline.Execute(context);
        Assert.True(result.Success, $"難読化に失敗 ({scenarioName}): {result.ErrorMessage}");

        // runtimeconfig をコピー
        var srcRc = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.runtimeconfig.json");
        var dstRc = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.obfuscated.runtimeconfig.json");
        if (File.Exists(srcRc))
            File.Copy(srcRc, dstRc, true);

        // 難読化後のアセンブリを --verify モードで実行
        var execResult = RunDotnetExec(outputDll, dstRc, "--verify");
        Assert.True(execResult.ExitCode == 0,
            $"難読化後の実行に失敗 ({scenarioName}, exit={execResult.ExitCode})\nStdErr: {execResult.StdErr}");

        var obfuscatedChecksum = ExtractChecksum(execResult.StdOut);
        Assert.False(string.IsNullOrEmpty(obfuscatedChecksum),
            $"難読化後のチェックサムが取得できません ({scenarioName})\nStdOut: {execResult.StdOut}");

        Assert.Equal(_baselineChecksum, obfuscatedChecksum);
    }

    private static string ExtractChecksum(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("CHECKSUM:"))
                return trimmed["CHECKSUM:".Length..];
        }
        return "";
    }

    private static (int ExitCode, string StdOut, string StdErr) RunDotnetExec(
        string dllPath, string runtimeConfigPath, string? args = null)
    {
        var arguments = $"exec --runtimeconfig \"{runtimeConfigPath}\" \"{dllPath}\"";
        if (args != null)
            arguments += $" {args}";
        return RunProcess("dotnet", arguments);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            return (-1, stdout, "Process timed out");
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "netbfsctn.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "netbfsctn.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Cannot find repository root");
    }
}
