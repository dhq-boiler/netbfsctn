using System.Diagnostics;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL;

namespace Netbfsctn.Tests;

/// <summary>
/// WPF アプリケーションの難読化テスト。
/// SampleWpfApp を各テクニック構成で難読化し、--verify モードで実行して
/// ロジックが正しく動作することを確認する。
/// </summary>
public class WpfObfuscationTests : IDisposable
{
	private readonly string _tempDir;
	private readonly string _publishDir;
	private readonly string _sampleDll;
	private readonly string _runtimeConfig;
	private readonly bool _sampleAvailable;

	public WpfObfuscationTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "netbfsctn_wpf_test_" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(_tempDir);

		_publishDir = Path.Combine(_tempDir, "original");
		var repoRoot = FindRepoRoot();
		var sampleProj = Path.Combine(repoRoot, "tests", "Netbfsctn.Tests.SampleWpfApp",
			"Netbfsctn.Tests.SampleWpfApp.csproj");

		if (File.Exists(sampleProj))
		{
			var publishResult = RunProcess("dotnet",
				$"publish \"{sampleProj}\" -c Release -o \"{_publishDir}\" -v q --nologo");
			_sampleAvailable = publishResult.ExitCode == 0;
		}

		_sampleDll = Path.Combine(_publishDir, "Netbfsctn.Tests.SampleWpfApp.dll");
		_runtimeConfig = Path.Combine(_publishDir, "Netbfsctn.Tests.SampleWpfApp.runtimeconfig.json");
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, true); } catch { }
	}

	[Fact]
	public void Baseline_WpfApp_RunsCorrectly()
	{
		if (!_sampleAvailable) return;

		var result = RunDotnetExec(_sampleDll, _runtimeConfig, "--verify");
		Assert.Equal(0, result.ExitCode);
		Assert.Contains("OK", result.StdOut);
	}

	[Fact]
	public void Obfuscated_NoRename_RenameFields_RunsCorrectly()
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
		}, "no_rename_fields");

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
	public void Obfuscated_StringEncryptionOnly_RunsCorrectly()
	{
		if (!_sampleAvailable) return;

		var outputDll = ObfuscateWithOptions(new ObfuscationOptions
		{
			InputPath = _sampleDll,
			EnableRename = false,
			EnableStringEncryption = true,
			EnableControlFlow = false,
			EnableDeadCode = false,
		}, "str_only");

		AssertRunsCorrectly(outputDll);
	}

	[Fact]
	public void Obfuscated_AntiDebugAntiTamper_RunsCorrectly()
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
			EnableAntiTampering = true,
		}, "anti_debug_tamper");

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

	[Fact]
	public void Obfuscated_StreamDirectorFlags_RunsCorrectly()
	{
		if (!_sampleAvailable) return;

		// STREAM DIRECTOR で使用しているフラグの組み合わせを再現
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
		}, "stream_director");

		AssertRunsCorrectly(outputDll);
	}

	// --- Helper methods ---

	private string ObfuscateWithOptions(ObfuscationOptions baseOptions, string scenarioName)
	{
		var scenarioDir = Path.Combine(_tempDir, scenarioName);
		Directory.CreateDirectory(scenarioDir);

		foreach (var file in Directory.GetFiles(_publishDir))
			File.Copy(file, Path.Combine(scenarioDir, Path.GetFileName(file)), true);

		var inputDll = Path.Combine(scenarioDir, "Netbfsctn.Tests.SampleWpfApp.dll");
		var outputDll = Path.Combine(scenarioDir, "Netbfsctn.Tests.SampleWpfApp.obfuscated.dll");

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

		var srcRc = Path.Combine(scenarioDir, "Netbfsctn.Tests.SampleWpfApp.runtimeconfig.json");
		var dstRc = Path.Combine(scenarioDir, "Netbfsctn.Tests.SampleWpfApp.obfuscated.runtimeconfig.json");
		if (File.Exists(srcRc))
			File.Copy(srcRc, dstRc, true);

		return outputDll;
	}

	private void AssertRunsCorrectly(string obfuscatedDll)
	{
		var runtimeConfig = Path.ChangeExtension(obfuscatedDll, null) + ".runtimeconfig.json";
		var result = RunDotnetExec(obfuscatedDll, runtimeConfig, "--verify");
		Assert.True(result.ExitCode == 0,
			$"難読化後のWPFアセンブリの実行に失敗 (exit code: {result.ExitCode})\nStdOut: {result.StdOut}\nStdErr: {result.StdErr}");
		Assert.Contains("OK", result.StdOut);
	}

	private static (int ExitCode, string StdOut, string StdErr) RunDotnetExec(
		string dllPath, string runtimeConfigPath, params string[] args)
	{
		var argsStr = string.Join(" ", args);
		var result = RunProcess("dotnet",
			$"exec --runtimeconfig \"{runtimeConfigPath}\" \"{dllPath}\" {argsStr}");
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
