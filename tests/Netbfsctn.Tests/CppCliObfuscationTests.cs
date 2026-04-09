using System.Diagnostics;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL;

namespace Netbfsctn.Tests;

/// <summary>
/// C++/CLI (混合モード) アセンブリの難読化テスト。
/// SampleCppCli ライブラリを難読化し、ハーネスアプリで動作検証する。
/// virtual メソッドリネームの問題を再現・検証するためのテスト群。
/// </summary>
public class CppCliObfuscationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cppCliDll;
    private readonly string _harnessDll;
    private readonly string _harnessRuntimeConfig;
    private readonly bool _available;

    public CppCliObfuscationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "netbfsctn_cppcli_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var repoRoot = FindRepoRoot();

        // Pre-built C++/CLI DLL location
        var cppCliDir = Path.Combine(repoRoot, "tests", "Netbfsctn.Tests.SampleCppCli",
            "tests", "Netbfsctn.Tests.SampleCppCli", "bin", "Release");
        _cppCliDll = Path.Combine(cppCliDir, "Netbfsctn.Tests.SampleCppCli.dll");

        // Harness app (C# console app that exercises the C++/CLI library)
        var harnessDir = Path.Combine(repoRoot, "tests", "Netbfsctn.Tests.CppCliHarness",
            "bin", "Release", "net10.0");
        _harnessDll = Path.Combine(harnessDir, "Netbfsctn.Tests.CppCliHarness.dll");
        _harnessRuntimeConfig = Path.Combine(harnessDir, "Netbfsctn.Tests.CppCliHarness.runtimeconfig.json");

        _available = File.Exists(_cppCliDll) && File.Exists(_harnessDll);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Baseline_CppCli_RunsCorrectly()
    {
        if (!_available) return;

        var workDir = PrepareWorkDir("baseline");
        var result = RunHarness(workDir);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CHECKSUM:6/6", result.StdOut);
    }

    [Fact]
    public void Obfuscated_RenamePublic_RunsCorrectly()
    {
        if (!_available) return;

        var workDir = PrepareWorkDir("rename_public");
        ObfuscateCppCliDll(workDir);
        var result = RunHarness(workDir);

        Assert.True(result.ExitCode == 0,
            $"難読化後の実行に失敗 (exit={result.ExitCode})\nStdOut: {result.StdOut}\nStdErr: {result.StdErr}");
        Assert.Contains("CHECKSUM:6/6", result.StdOut);
    }

    // --- Helper methods ---

    private string PrepareWorkDir(string scenarioName)
    {
        var workDir = Path.Combine(_tempDir, scenarioName);
        Directory.CreateDirectory(workDir);

        // Copy harness app files
        var harnessDir = Path.GetDirectoryName(_harnessDll)!;
        foreach (var file in Directory.GetFiles(harnessDir))
            File.Copy(file, Path.Combine(workDir, Path.GetFileName(file)), true);

        // Copy C++/CLI DLL and Ijwhost.dll (overwrite harness copies with fresh ones)
        var cppCliDir = Path.GetDirectoryName(_cppCliDll)!;
        File.Copy(_cppCliDll, Path.Combine(workDir, "Netbfsctn.Tests.SampleCppCli.dll"), true);
        var ijwhost = Path.Combine(cppCliDir, "Ijwhost.dll");
        if (File.Exists(ijwhost))
            File.Copy(ijwhost, Path.Combine(workDir, "Ijwhost.dll"), true);

        // Copy runtimeconfig and deps.json for the C++/CLI DLL
        var cppCliRc = Path.Combine(cppCliDir, "Netbfsctn.Tests.SampleCppCli.runtimeconfig.json");
        if (File.Exists(cppCliRc))
            File.Copy(cppCliRc, Path.Combine(workDir, "Netbfsctn.Tests.SampleCppCli.runtimeconfig.json"), true);
        var cppCliDeps = Path.Combine(cppCliDir, "Netbfsctn.Tests.SampleCppCli.deps.json");
        if (File.Exists(cppCliDeps))
            File.Copy(cppCliDeps, Path.Combine(workDir, "Netbfsctn.Tests.SampleCppCli.deps.json"), true);

        return workDir;
    }

    private void ObfuscateCppCliDll(string workDir)
    {
        var cppCliDll = Path.Combine(workDir, "Netbfsctn.Tests.SampleCppCli.dll");
        var cppCliOut = Path.Combine(workDir, "Netbfsctn.Tests.SampleCppCli.obfuscated.dll");
        var harnessDll = Path.Combine(workDir, "Netbfsctn.Tests.CppCliHarness.dll");
        var harnessOut = Path.Combine(workDir, "Netbfsctn.Tests.CppCliHarness.obfuscated.dll");

        var options = new ObfuscationOptions
        {
            InputPath = cppCliDll,
            OutputPath = cppCliOut,
            AdditionalInputPaths = [harnessDll],
            AdditionalOutputPaths = [harnessOut],
            EnableRename = true,
            EnableRenamePublic = true,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false,
            Verbose = true,
        };

        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };

        var pipeline = new ILObfuscationPipeline();
        var result = pipeline.Execute(context);

        Assert.True(result.Success, $"難読化に失敗: {result.ErrorMessage}");
        Assert.True(File.Exists(cppCliOut), $"C++/CLI出力が存在しない: {cppCliOut}");
        Assert.True(File.Exists(harnessOut), $"ハーネス出力が存在しない: {harnessOut}");

        // Replace originals with obfuscated
        File.Delete(cppCliDll);
        File.Move(cppCliOut, cppCliDll);
        File.Delete(harnessDll);
        File.Move(harnessOut, harnessDll);
    }

    private (int ExitCode, string StdOut, string StdErr) RunHarness(string workDir)
    {
        var harnessDll = Path.Combine(workDir, "Netbfsctn.Tests.CppCliHarness.dll");
        var runtimeConfig = Path.Combine(workDir, "Netbfsctn.Tests.CppCliHarness.runtimeconfig.json");

        return RunProcess("dotnet",
            $"exec --runtimeconfig \"{runtimeConfig}\" \"{harnessDll}\" --verify");
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
