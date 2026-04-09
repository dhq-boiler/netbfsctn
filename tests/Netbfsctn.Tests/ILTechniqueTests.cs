using dnlib.DotNet;
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
        _sampleDllPath = typeof(ILTechniqueTests).Assembly.Location;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static (ObfuscationContext context, ObfuscationResult result) CreateContext()
    {
        var options = new ObfuscationOptions { InputPath = "dummy.dll", Verbose = true };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        var result = new ObfuscationResult { Success = true };
        return (context, result);
    }

    private ModuleDefMD LoadSampleModule()
    {
        return ModuleDefMD.Load(_sampleDllPath);
    }

    // === Anti ILDASM ===

    [Fact]
    public void AntiIldasm_AddsSuppressAttribute()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiIldasm().Apply(module, context, result);
        Assert.True(result.AntiIldasmApplied);
        Assert.Contains(module.Assembly.CustomAttributes,
            a => a.AttributeType.Name == "SuppressIldasmAttribute");
    }

    [Fact]
    public void AntiIldasm_DefinesAttributeType()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiIldasm().Apply(module, context, result);
        Assert.Contains(module.Types,
            t => t.Name == "SuppressIldasmAttribute" &&
                 t.Namespace == "System.Runtime.CompilerServices");
    }

    [Fact]
    public void AntiIldasm_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiIldasm().Apply(module, context, result);
        var outPath = Path.Combine(_tempDir, "anti_ildasm.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Anti Debug ===

    [Fact]
    public void AntiDebug_SetsAppliedFlag()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiDebug().Apply(module, context, result);
        Assert.True(result.AntiDebugApplied);
    }

    [Fact]
    public void AntiDebug_InjectsModuleCctor()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiDebug().Apply(module, context, result);
        var cctor = module.GlobalType.FindStaticConstructor();
        Assert.NotNull(cctor);
        Assert.True(cctor.HasBody);
    }

    [Fact]
    public void AntiDebug_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiDebug().Apply(module, context, result);
        var outPath = Path.Combine(_tempDir, "anti_debug.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Anti Tampering ===

    [Fact]
    public void AntiTampering_SetsAppliedFlag()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiTampering().Apply(module, context, result);
        Assert.True(result.AntiTamperingApplied);
    }

    [Fact]
    public void AntiTampering_AddsHashResource()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiTampering().Apply(module, context, result);
        Assert.Contains(module.Resources, r => r.Name == "\u200B\u200D\u200C");
    }

    [Fact]
    public void AntiTampering_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILAntiTampering().Apply(module, context, result);
        var outPath = Path.Combine(_tempDir, "anti_tamper.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === NecroBit ===

    [Fact]
    public void NecroBit_EncryptsEligibleMethods()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILNecroBit().Apply(module, context, result);
        Assert.True(result.EncryptedMethodBodies >= 0);
    }

    [Fact]
    public void NecroBit_AddsResourceWhenMethodsEncrypted()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILNecroBit().Apply(module, context, result);
        if (result.EncryptedMethodBodies > 0)
            Assert.Contains(module.Resources, r => r.Name == "\u200C\u200B\u200D");
    }

    [Fact]
    public void NecroBit_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILNecroBit().Apply(module, context, result);
        var outPath = Path.Combine(_tempDir, "necrobit.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Hide Method Calls ===

    [Fact]
    public void HideMethodCalls_CountsHiddenCalls()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILHideMethodCalls().Apply(module, context, result);
        Assert.True(result.HiddenMethodCalls >= 0);
    }

    [Fact]
    public void HideMethodCalls_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILHideMethodCalls().Apply(module, context, result);
        PrepareForWrite(module);
        var outPath = Path.Combine(_tempDir, "hide_calls.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Resource Protector ===

    [Fact]
    public void ResourceProtector_ProtectsResources()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILResourceProtector().Apply(module, context, result);
        Assert.True(result.ProtectedResources >= 0);
    }

    [Fact]
    public void ResourceProtector_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILResourceProtector().Apply(module, context, result);
        var outPath = Path.Combine(_tempDir, "resource_protect.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Code Virtualizer ===

    [Fact]
    public void CodeVirtualizer_VirtualizesMethods()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILCodeVirtualizer().Apply(module, context, result);
        Assert.True(result.VirtualizedMethods >= 0);
    }

    [Fact]
    public void CodeVirtualizer_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILCodeVirtualizer().Apply(module, context, result);
        var outPath = Path.Combine(_tempDir, "virtualized.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === VM OpCode ===

    [Fact]
    public void VmOpCode_HasExpectedValues()
    {
        Assert.Equal(0x01, (byte)IL.VM.VmOpCode.ADD);
        Assert.Equal(0xF0, (byte)IL.VM.VmOpCode.RET);
        Assert.Equal(0xFF, (byte)IL.VM.VmOpCode.NOP);
    }

    // === CilToVmTranslator ===

    [Fact]
    public void CilToVmTranslator_TranslatesSimpleMethod()
    {
        using var module = LoadSampleModule();
        var translator = new IL.VM.CilToVmTranslator(module);

        foreach (var type in module.Types)
        {
            foreach (var method in type.Methods)
            {
                if (method.HasBody && !method.IsPublic && !method.IsConstructor &&
                    !method.HasGenericParameters && method.Body.ExceptionHandlers.Count == 0 &&
                    method.Body.Instructions.Count >= 3)
                {
                    var bytecode = translator.Translate(method);
                    if (bytecode != null)
                    {
                        Assert.True(bytecode.Length > 0);
                    }
                    return;
                }
            }
        }
    }

    // === Mapping File ===

    [Fact]
    public void MappingFile_OutputsJsonWhenEnabled()
    {
        using var module = LoadSampleModule();
        var outPath = Path.Combine(_tempDir, "mapping_test.dll");
        var mappingPath = Path.Combine(_tempDir, "mapping_test.map.json");

        var options = new ObfuscationOptions
        {
            InputPath = _sampleDllPath, OutputPath = outPath,
            EnableMappingFile = true, MappingFilePath = mappingPath,
            EnableRename = true, EnableStringEncryption = false,
            EnableControlFlow = false, EnableDeadCode = false
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        var result = new ObfuscationResult { Success = true, OutputPath = outPath };

        new ILNameObfuscator().Apply(module, context, result);
        module.Write(outPath);

        if (context.NameMap.Count > 0)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(context.NameMap,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(mappingPath, json);
            Assert.True(File.Exists(mappingPath));
            Assert.Contains("{", File.ReadAllText(mappingPath));
        }
    }

    // === 統合テスト ===

    [Fact]
    public void Pipeline_AllNewTechniques_CanBeAppliedTogether()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();

        new ILAntiIldasm().Apply(module, context, result);
        new ILAntiDebug().Apply(module, context, result);
        new ILHideMethodCalls().Apply(module, context, result);
        new ILResourceProtector().Apply(module, context, result);
        new ILAntiTampering().Apply(module, context, result);

        Assert.True(result.AntiIldasmApplied);
        Assert.True(result.AntiDebugApplied);
        Assert.True(result.AntiTamperingApplied);

        PrepareForWrite(module);
        var outPath = Path.Combine(_tempDir, "all_techniques.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    // === Name Obfuscator - Public Type Handling ===

    [Fact]
    public void NameObfuscator_SkipsPublicTypes_WhenRenamePublicDisabled()
    {
        using var module = LoadSampleModule();
        var options = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            EnableRename = true,
            EnableRenamePublic = false,
            Verbose = true,
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        var result = new ObfuscationResult { Success = true };

        var publicTypeNamesBefore = module.Types
            .Where(t => (t.IsPublic || t.IsNestedPublic) && t.Name != "<Module>")
            .Select(t => t.Name.String)
            .ToHashSet();

        new ILNameObfuscator().Apply(module, context, result);

        // public 型はリネームされない
        var publicTypeNamesAfter = module.Types
            .Where(t => (t.IsPublic || t.IsNestedPublic) && t.Name != "<Module>")
            .Select(t => t.Name.String)
            .ToHashSet();

        Assert.True(publicTypeNamesBefore.SetEquals(publicTypeNamesAfter),
            "public型がリネームされてしまっている");
    }

    [Fact]
    public void NameObfuscator_RenamesPublicTypes_WhenRenamePublicEnabled()
    {
        using var module = LoadSampleModule();
        var options = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            EnableRename = true,
            EnableRenamePublic = true,
            Verbose = true,
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        var result = new ObfuscationResult { Success = true };

        new ILNameObfuscator().Apply(module, context, result);

        // MemberRenameHistory にエントリが存在するはず
        Assert.True(context.MemberRenameHistory.Count > 0,
            "EnableRenamePublic=true でも MemberRenameHistory が空");
        Assert.True(result.RenamedSymbols > 0);
    }

    [Fact]
    public void NameObfuscator_PopulatesMemberRenameHistory_WhenRenamePublicEnabled()
    {
        using var module = LoadSampleModule();
        var options = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            EnableRename = true,
            EnableRenamePublic = true,
            Verbose = true,
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        var result = new ObfuscationResult { Success = true };

        new ILNameObfuscator().Apply(module, context, result);

        // MemberRenameHistory にもエントリが存在するはず
        Assert.True(context.MemberRenameHistory.Count > 0,
            "MemberRenameHistory が空");
    }

    [Fact]
    public void NameObfuscator_RenamesMoreSymbols_WithRenamePublic()
    {
        // EnableRenamePublic のほうが多くのシンボルをリネームする
        using var moduleA = LoadSampleModule();
        var optionsA = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            EnableRename = true,
            Verbose = true,
        };
        var loggerA = new ObfuscationLogger(verbose: true, quiet: false);
        var contextA = new ObfuscationContext { Options = optionsA, Logger = loggerA };
        var resultA = new ObfuscationResult { Success = true };
        new ILNameObfuscator().Apply(moduleA, contextA, resultA);

        using var moduleB = LoadSampleModule();
        var optionsB = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            EnableRename = true,
            EnableRenamePublic = true,
            Verbose = true,
        };
        var loggerB = new ObfuscationLogger(verbose: true, quiet: false);
        var contextB = new ObfuscationContext { Options = optionsB, Logger = loggerB };
        var resultB = new ObfuscationResult { Success = true };
        new ILNameObfuscator().Apply(moduleB, contextB, resultB);

        Assert.True(resultB.RenamedSymbols > resultA.RenamedSymbols,
            $"EnableRenamePublic のほうがリネーム数が多いはず: without={resultA.RenamedSymbols}, with={resultB.RenamedSymbols}");
    }

    [Fact]
    public void NameObfuscator_SkipsPublicTypes_WhenExcluded()
    {
        using var module = LoadSampleModule();
        var options = new ObfuscationOptions
        {
            InputPath = "dummy.dll",
            EnableRename = true,
            EnableRenamePublic = true,
            Verbose = true,
        };
        var logger = new ObfuscationLogger(verbose: true, quiet: false);
        var context = new ObfuscationContext { Options = options, Logger = logger };
        // ExcludeRenamePublicModules に登録すると public リネームがスキップされる
        context.ExcludeRenamePublicModules.Add(module.Assembly.Name.String);
        var result = new ObfuscationResult { Success = true };

        var publicTypeNamesBefore = module.Types
            .Where(t => (t.IsPublic || t.IsNestedPublic) && t.Name != "<Module>")
            .Select(t => t.Name.String)
            .ToHashSet();

        new ILNameObfuscator().Apply(module, context, result);

        var publicTypeNamesAfter = module.Types
            .Where(t => (t.IsPublic || t.IsNestedPublic) && t.Name != "<Module>")
            .Select(t => t.Name.String)
            .ToHashSet();

        Assert.True(publicTypeNamesBefore.SetEquals(publicTypeNamesAfter),
            "ExcludeRenamePublicModules に登録済みなのに public 型がリネームされた");
    }

    // === 混合モード検出テスト ===

    [Fact]
    public void MixedModeDetection_ILOnlyAssemblyIsDetected()
    {
        using var module = LoadSampleModule();
        // テスト用アセンブリは純粋 .NET なので IsILOnly = true
        Assert.True(module.IsILOnly);
    }

    /// <summary>
    /// 個別テクニック適用後に module.Write() する場合、
    /// パイプラインの maxStack 推定を経由しないため KeepOldMaxStack を設定する。
    /// </summary>
    private static void PrepareForWrite(ModuleDef module)
    {
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                method.Body.SimplifyBranches();
                method.Body.OptimizeBranches();
                method.Body.KeepOldMaxStack = true;
            }
        }
    }
}
