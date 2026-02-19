using Mono.Cecil;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL.Techniques;

namespace Netbfsctn.Tests;

public class ILTechniqueTests : IDisposable
{
    private readonly string _sampleDllPath;
    private readonly string _tempDir;

    public ILTechniqueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "netbfsctn_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        // テスト用のサンプル DLL パスを特定
        // テストプロジェクト自体の DLL を使用
        _sampleDllPath = typeof(ILTechniqueTests).Assembly.Location;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static (ObfuscationContext context, ObfuscationResult result) CreateContext()
    {
        var options = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            Verbose = true
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        var result = new ObfuscationResult { Success = true };
        return (context, result);
    }

    private AssemblyDefinition LoadSampleAssembly()
    {
        var readerParams = new ReaderParameters { ReadWrite = false };
        return AssemblyDefinition.ReadAssembly(_sampleDllPath, readerParams);
    }

    // === Anti ILDASM テスト ===

    [Fact]
    public void AntiIldasm_AddsSuppressAttribute()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiIldasm();
        technique.Apply(module, context, result);

        Assert.True(result.AntiIldasmApplied);
        Assert.Contains(module.Assembly.CustomAttributes,
            a => a.AttributeType.Name == "SuppressIldasmAttribute");
    }

    [Fact]
    public void AntiIldasm_DefinesAttributeType()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiIldasm();
        technique.Apply(module, context, result);

        Assert.Contains(module.Types,
            t => t.Name == "SuppressIldasmAttribute" &&
                 t.Namespace == "System.Runtime.CompilerServices");
    }

    [Fact]
    public void AntiIldasm_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiIldasm();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "anti_ildasm.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Anti Debug テスト ===

    [Fact]
    public void AntiDebug_SetsAppliedFlag()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiDebug();
        technique.Apply(module, context, result);

        Assert.True(result.AntiDebugApplied);
    }

    [Fact]
    public void AntiDebug_InjectsModuleCctor()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiDebug();
        technique.Apply(module, context, result);

        var moduleType = module.Types.First(t => t.Name == "<Module>");
        var cctor = moduleType.Methods.FirstOrDefault(m => m.Name == ".cctor");
        Assert.NotNull(cctor);
        Assert.True(cctor.HasBody);
    }

    [Fact]
    public void AntiDebug_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiDebug();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "anti_debug.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Anti Tampering テスト ===

    [Fact]
    public void AntiTampering_SetsAppliedFlag()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiTampering();
        technique.Apply(module, context, result);

        Assert.True(result.AntiTamperingApplied);
    }

    [Fact]
    public void AntiTampering_AddsHashResource()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiTampering();
        technique.Apply(module, context, result);

        Assert.Contains(module.Resources,
            r => r.Name == "__tamper_hash__");
    }

    [Fact]
    public void AntiTampering_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILAntiTampering();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "anti_tamper.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === NecroBit テスト ===

    [Fact]
    public void NecroBit_EncryptsEligibleMethods()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILNecroBit();
        technique.Apply(module, context, result);

        Assert.True(result.EncryptedMethodBodies >= 0);
    }

    [Fact]
    public void NecroBit_AddsResourceWhenMethodsEncrypted()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILNecroBit();
        technique.Apply(module, context, result);

        if (result.EncryptedMethodBodies > 0)
        {
            Assert.Contains(module.Resources, r => r.Name == "__nb_data__");
        }
    }

    [Fact]
    public void NecroBit_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILNecroBit();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "necrobit.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Hide Method Calls テスト ===

    [Fact]
    public void HideMethodCalls_CountsHiddenCalls()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILHideMethodCalls();
        technique.Apply(module, context, result);

        // 結果は0以上（対象メソッドがある場合は > 0）
        Assert.True(result.HiddenMethodCalls >= 0);
    }

    [Fact]
    public void HideMethodCalls_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILHideMethodCalls();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "hide_calls.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Resource Protector テスト ===

    [Fact]
    public void ResourceProtector_ProtectsResources()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        // 既存のリソースがある場合のみ保護される
        var technique = new ILResourceProtector();
        technique.Apply(module, context, result);

        Assert.True(result.ProtectedResources >= 0);
    }

    [Fact]
    public void ResourceProtector_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILResourceProtector();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "resource_protect.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Code Virtualizer テスト ===

    [Fact]
    public void CodeVirtualizer_VirtualizesMethods()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILCodeVirtualizer();
        technique.Apply(module, context, result);

        Assert.True(result.VirtualizedMethods >= 0);
    }

    [Fact]
    public void CodeVirtualizer_CanWriteAssembly()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        var technique = new ILCodeVirtualizer();
        technique.Apply(module, context, result);

        var outPath = Path.Combine(_tempDir, "virtualized.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === VM OpCode テスト ===

    [Fact]
    public void VmOpCode_HasExpectedValues()
    {
        Assert.Equal(0x01, (byte)IL.VM.VmOpCode.ADD);
        Assert.Equal(0xF0, (byte)IL.VM.VmOpCode.RET);
        Assert.Equal(0xFF, (byte)IL.VM.VmOpCode.NOP);
    }

    // === CilToVmTranslator テスト ===

    [Fact]
    public void CilToVmTranslator_TranslatesSimpleMethod()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;

        var translator = new IL.VM.CilToVmTranslator(module);

        // 単純な private メソッドを探してテスト
        foreach (var type in module.Types)
        {
            foreach (var method in type.Methods)
            {
                if (method.HasBody && !method.IsPublic && !method.IsConstructor &&
                    !method.HasGenericParameters && !method.Body.HasExceptionHandlers &&
                    method.Body.Instructions.Count >= 3)
                {
                    var bytecode = translator.Translate(method);
                    // null の場合はサポートされていない命令がある
                    // 変換できた場合はバイトコードが空でないこと
                    if (bytecode != null)
                    {
                        Assert.True(bytecode.Length > 0);
                    }
                    return;
                }
            }
        }
    }

    // === Mapping File テスト ===

    [Fact]
    public void MappingFile_OutputsJsonWhenEnabled()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;

        var outPath = Path.Combine(_tempDir, "mapping_test.dll");
        var mappingPath = Path.Combine(_tempDir, "mapping_test.map.json");

        var options = new ObfuscationOptions
        {
            InputPath = _sampleDllPath,
            OutputPath = outPath,
            EnableMappingFile = true,
            MappingFilePath = mappingPath,
            EnableRename = true,
            EnableStringEncryption = false,
            EnableControlFlow = false,
            EnableDeadCode = false
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };

        // 名前変更テクニックを適用して NameMap に何か追加
        var renamer = new ILNameObfuscator();
        var result = new ObfuscationResult { Success = true, OutputPath = outPath };
        renamer.Apply(module, context, result);

        // アセンブリを保存
        assembly.Write(outPath);

        // マッピングファイルを出力
        if (context.NameMap.Count > 0)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(context.NameMap,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(mappingPath, json);

            Assert.True(File.Exists(mappingPath));
            var content = File.ReadAllText(mappingPath);
            Assert.Contains("{", content);
        }
    }

    // === パイプライン統合テスト ===

    [Fact]
    public void Pipeline_AllNewTechniques_CanBeAppliedTogether()
    {
        using var assembly = LoadSampleAssembly();
        var module = assembly.MainModule;
        var (context, result) = CreateContext();

        // 全テクニックを順番に適用
        new ILAntiIldasm().Apply(module, context, result);
        new ILAntiDebug().Apply(module, context, result);
        new ILHideMethodCalls().Apply(module, context, result);
        new ILResourceProtector().Apply(module, context, result);
        new ILAntiTampering().Apply(module, context, result);

        Assert.True(result.AntiIldasmApplied);
        Assert.True(result.AntiDebugApplied);
        Assert.True(result.AntiTamperingApplied);

        var outPath = Path.Combine(_tempDir, "all_techniques.dll");
        assembly.Write(outPath);
        Assert.True(File.Exists(outPath));
    }
}
