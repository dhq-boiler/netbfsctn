using System.Diagnostics;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL;

namespace Netbfsctn.Tests;

/// <summary>
/// 難読化後のアセンブリが実際に実行可能かを検証する統合テスト。
/// SampleApp を各テクニック構成で難読化し、dotnet exec で実行して
/// InvalidProgramException 等が発生しないことを確認する。
/// </summary>
public class ILPipelineRuntimeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sampleAppDir;
    private readonly string _sampleDll;
    private readonly string _runtimeConfig;
    private readonly bool _sampleAvailable;

    public ILPipelineRuntimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "netbfsctn_runtime_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        // SampleApp を publish
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
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Baseline_SampleApp_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var result = RunDotnetExec(_sampleDll, _runtimeConfig);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Obfuscated_DefaultOptions_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
        }, "default");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_ControlFlowOnly_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = true,
            EnableDeadCode = false,
        }, "cf_only");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_DeadCodeOnly_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = true,
        }, "dc_only");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_ControlFlowPlusDeadCode_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = true,
            EnableDeadCode = true,
        }, "cf_dc");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_AntiDebug_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableAntiDebug = true,
        }, "anti_debug");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_AntiTamper_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableAntiTampering = true,
        }, "anti_tamper");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_NecroBit_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableNecroBit = true,
        }, "necrobit");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_NecroBit_WithDeadCode_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        // DeadCode injects dummy methods that NecroBit should be able to encrypt.
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = true,
            EnableNecroBit = true,
        }, "necrobit_dc");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_ResourceProtection_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableResourceProtection = true,
        }, "res_protect");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_FullPipeline_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
            EnableAntiDebug = true,
            EnableAntiTampering = true,
            EnableResourceProtection = true,
        }, "full");

        AssertRunsCorrectly(outputDll);
    }

    /// <summary>
    /// ユーザー報告の再現ケース: --no-rename --rename-fields --protect-resources --anti-debug --anti-tamper
    /// </summary>
    [Fact]
    public void Obfuscated_UserReportedFlags_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableRenameFields = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
            EnableAntiDebug = true,
            EnableAntiTampering = true,
            EnableResourceProtection = true,
        }, "user_reported");

        AssertRunsCorrectly(outputDll);
    }

    // --- Helper methods ---

    private string ObfuscateWithOptions(ObfuscationOptions baseOptions, string scenarioName)
    {
        var scenarioDir = Path.Combine(_tempDir, scenarioName);
        Directory.CreateDirectory(scenarioDir);

        // コピー
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

        Assert.True(result.Success, $"難読化に失敗: {result.ErrorMessage}");
        Assert.True(File.Exists(outputDll), $"出力ファイルが存在しない: {outputDll}");

        // runtimeconfig をコピー
        var srcRc = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.runtimeconfig.json");
        var dstRc = Path.Combine(scenarioDir, "Netbfsctn.Benchmark.SampleApp.obfuscated.runtimeconfig.json");
        if (File.Exists(srcRc))
            File.Copy(srcRc, dstRc, true);

        return outputDll;
    }

    private void AssertRunsCorrectly(string obfuscatedDll)
    {
        var runtimeConfig = Path.ChangeExtension(obfuscatedDll, null) + ".runtimeconfig.json";
        var result = RunDotnetExec(obfuscatedDll, runtimeConfig);
        Assert.True(result.ExitCode == 0,
            $"難読化後のアセンブリの実行に失敗 (exit code: {result.ExitCode})\nStdOut: {result.StdOut}\nStdErr: {result.StdErr}");
    }

    private static (int ExitCode, string StdOut, string StdErr) RunDotnetExec(string dllPath, string runtimeConfigPath)
    {
        var result = RunProcess("dotnet",
            $"exec --runtimeconfig \"{runtimeConfigPath}\" \"{dllPath}\"");
        return result;
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
