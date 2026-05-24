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
    public void Obfuscated_AntiTamper_OutputMatchesBaseline()
    {
        if (!_sampleAvailable) return;

        // AntiTampering の構造ハッシュ計算 (obfuscation 側) と runtime 検証 (注入 IL 側) が
        // 一致せず偽陽性で FailFast すると exit code は 0 のままなので、stdout 一致まで確認する。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableAntiTampering = true,
        }, "anti_tamper_match");

        AssertOutputMatchesBaseline(outputDll);
    }

    [Fact]
    public void Obfuscated_AntiTamper_DetectsAddedMethod()
    {
        if (!_sampleAvailable) return;

        // AntiTampering を有効にした後、dnlib で新規メソッドを追加して改ざんし、
        // 実行時に FailFast が発火することを確認する。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableAntiTampering = true,
        }, "anti_tamper_detect");

        // 改ざん版は同じディレクトリに置くと .NET runtime が同 assembly identity の
        // 別ファイルを resolve してしまい check method が無効化される。隔離 dir に書く。
        var tamperedDir = Path.Combine(_tempDir, "anti_tamper_detect_isolated");
        Directory.CreateDirectory(tamperedDir);
        var tamperedDll = Path.Combine(tamperedDir, Path.GetFileName(outputDll));

        using (var module = dnlib.DotNet.ModuleDefMD.Load(outputDll))
        {
            var targetType = module.GetTypes()
                .FirstOrDefault(t => t.FullName == "Netbfsctn.Benchmark.SampleApp.Models.Order");
            Assert.NotNull(targetType);

            var newMethod = new dnlib.DotNet.MethodDefUser(
                "TamperedMethod",
                dnlib.DotNet.MethodSig.CreateInstance(module.CorLibTypes.Void),
                dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.HideBySig);
            var b = new dnlib.DotNet.Emit.CilBody();
            b.Instructions.Add(new dnlib.DotNet.Emit.Instruction(dnlib.DotNet.Emit.OpCodes.Ret));
            newMethod.Body = b;
            targetType.Methods.Add(newMethod);

            module.Write(tamperedDll);
        }

        // runtimeconfig と deps.json を隔離 dir にもコピー
        var srcDir = Path.GetDirectoryName(outputDll)!;
        var rcName = Path.GetFileNameWithoutExtension(outputDll) + ".runtimeconfig.json";
        var depsName = Path.GetFileNameWithoutExtension(outputDll) + ".deps.json";
        var origRc = Path.Combine(srcDir, rcName);
        var origDeps = Path.Combine(srcDir, depsName);
        if (File.Exists(origRc)) File.Copy(origRc, Path.Combine(tamperedDir, rcName), true);
        if (File.Exists(origDeps)) File.Copy(origDeps, Path.Combine(tamperedDir, depsName), true);
        var tamperedRc = Path.Combine(tamperedDir, rcName);

        var result = RunDotnetExec(tamperedDll, tamperedRc);

        // Environment.FailFast は Windows では exit code 0 のままになることがあるが、
        // stderr/stdout に "Process terminated" / "FailFast" メッセージが出る。
        // 通常出力 (Customer: Taro... 等) は出ないことも合わせて確認する。
        var combined = result.StdOut + result.StdErr;
        Assert.True(combined.Contains("FailFast"),
            $"FailFast expected, got:\nExit={result.ExitCode}\nStdOut={result.StdOut}\nStdErr={result.StdErr}\nTamperedDll={tamperedDll}");
        Assert.DoesNotContain("Customer: Taro Yamada", combined);
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
    public void Obfuscated_ResourceProtection_DecryptsAtRuntime()
    {
        if (!_sampleAvailable) return;

        // ResourceProtector は GetManifestResourceStream 呼び出しを書き換えて
        // 暗号化リソースを透過復号する必要がある。出力が baseline と完全一致するか確認。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableResourceProtection = true,
        }, "res_protect_decrypt");

        AssertOutputMatchesBaseline(outputDll);
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

    [Fact]
    public void Obfuscated_MbaOnly_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableMixedBooleanArithmetic = true,
        }, "mba_only");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_MbaWithDeadCode_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = true,
            EnableMixedBooleanArithmetic = true,
        }, "mba_dc");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_MbaWithNecroBitAndVirtualize_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        // MBA は helper 注入技法より前に実行されるため、NecroBit / Virtualize が
        // 注入したヘルパーメソッドの IL は破壊されないことを確認する。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableNecroBit = true,
            EnableCodeVirtualization = true,
            EnableMixedBooleanArithmetic = true,
        }, "mba_necrobit_vm");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_VirtualizeOnly_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        // Virtualize 単独: 翻訳できないメソッドはスキップされ、翻訳されたものだけが
        // VM 実行に回るため、stack 例外などの runtime エラーが発生しないこと。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableCodeVirtualization = true,
        }, "vm_only");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_NecroBitWithVirtualize_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        // NecroBit スタブ (Newarr + Call + Unbox_Any) は VM 非対応 opcode を含むので
        // 翻訳が拒否され、Virtualize がスキップする。両方同時に有効でも壊れないこと。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableNecroBit = true,
            EnableCodeVirtualization = true,
        }, "necrobit_vm");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_AllOptOutFeatures_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        // README にある "Full protection" 構成。全 opt-in 機能を併用しても壊れないこと。
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = true,
            EnableStringEncryption = true,
            EnableControlFlow = true,
            EnableDeadCode = true,
            EnableAntiIldasm = true,
            EnableAntiDebug = true,
            EnableAntiTampering = true,
            EnableNecroBit = true,
            EnableHideMethodCalls = true,
            EnableCodeVirtualization = true,
            EnableResourceProtection = true,
        }, "full_optin");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_DynamicOpaqueOnly_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableDynamicOpaquePredicates = true,
        }, "dop_only");

        AssertRunsCorrectly(outputDll);
    }

    [Fact]
    public void Obfuscated_AllPhase123_RunsCorrectly()
    {
        if (!_sampleAvailable) return;

        // Phase 1/2/3 の3つの新技法 + NecroBit + Virtualize の組み合わせ
        var outputDll = ObfuscateWithOptions(new ObfuscationOptions
        {
            InputPath = _sampleDll,
            EnableRename = false,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            EnableMixedBooleanArithmetic = true,
            EnableDynamicOpaquePredicates = true,
            EnableNecroBit = true,
            EnableCodeVirtualization = true,
        }, "phase123");

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
            EnableMixedBooleanArithmetic = baseOptions.EnableMixedBooleanArithmetic,
            EnableDynamicOpaquePredicates = baseOptions.EnableDynamicOpaquePredicates,
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

    /// <summary>
    /// 難読化後の標準出力が baseline (元アセンブリ実行結果) と完全一致することを検証する。
    /// 単に exit code 0 を確認するだけでは "壊れたデータを返すが例外は出ない" 系のバグを見逃すため。
    /// </summary>
    private void AssertOutputMatchesBaseline(string obfuscatedDll)
    {
        var runtimeConfig = Path.ChangeExtension(obfuscatedDll, null) + ".runtimeconfig.json";
        var obfResult = RunDotnetExec(obfuscatedDll, runtimeConfig);
        Assert.Equal(0, obfResult.ExitCode);

        var baselineResult = RunDotnetExec(_sampleDll, _runtimeConfig);
        Assert.Equal(0, baselineResult.ExitCode);

        Assert.Equal(baselineResult.StdOut, obfResult.StdOut);
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
