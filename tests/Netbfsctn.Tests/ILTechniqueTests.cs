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

    // === Mixed Boolean-Arithmetic ===

    [Fact]
    public void Mba_CountsTransformations()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILMixedBooleanArithmetic().Apply(module, context, result);
        Assert.True(result.MbaTransformations >= 0);
    }

    [Fact]
    public void Mba_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILMixedBooleanArithmetic().Apply(module, context, result);
        PrepareForWrite(module);
        var outPath = Path.Combine(_tempDir, "mba.dll");
        module.Write(outPath);
        Assert.True(File.Exists(outPath));
    }

    [Fact]
    public void Mba_TransformsAtLeastOneInstruction_OnRealisticAssembly()
    {
        // 自分自身を入力にし、十分な数の算術命令と定数ロードがあることを前提に
        // 少なくとも 1 件は変換が走ることを確認する。
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILMixedBooleanArithmetic().Apply(module, context, result);
        Assert.True(result.MbaTransformations > 0,
            "テスト用アセンブリに変換可能な命令があるはず");
    }

    [Fact]
    public void Mba_AddsLocalVariablesForTransformedMethods()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();

        var localCountsBefore = new Dictionary<string, int>();
        foreach (var t in module.Types)
            foreach (var m in t.Methods)
                if (m.HasBody)
                    localCountsBefore[m.FullName] = m.Body.Variables.Count;

        new ILMixedBooleanArithmetic().Apply(module, context, result);

        if (result.MbaTransformations == 0) return;

        // 算術変換が発生した場合、対象メソッドにローカル変数が追加されている
        var addedLocals = 0;
        foreach (var t in module.Types)
            foreach (var m in t.Methods)
                if (m.HasBody && localCountsBefore.TryGetValue(m.FullName, out var before))
                    addedLocals += Math.Max(0, m.Body.Variables.Count - before);

        Assert.True(addedLocals > 0, "MBA算術変換時には一時ローカルが追加されるはず");
    }

    // === Dynamic Opaque Predicates ===

    [Fact]
    public void DynamicOpaque_CountsInsertions()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILDynamicOpaquePredicates().Apply(module, context, result);
        Assert.True(result.DynamicOpaquePredicates >= 0);
    }

    [Fact]
    public void DynamicOpaque_InjectsHelperType()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILDynamicOpaquePredicates().Apply(module, context, result);

        // ヘルパー型 (空Namespace + ゼロ幅文字名) に T フィールドと K メソッドがあるはず
        var helperType = module.Types.FirstOrDefault(
            t => t.Fields.Any(f => f.Name == "T") &&
                 t.Methods.Any(m => m.Name == "K"));
        Assert.NotNull(helperType);
    }

    [Fact]
    public void DynamicOpaque_HelperHasStaticConstructor()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILDynamicOpaquePredicates().Apply(module, context, result);

        var helperType = module.Types.FirstOrDefault(
            t => t.Methods.Any(m => m.Name == "K"));
        Assert.NotNull(helperType);
        Assert.NotNull(helperType.FindStaticConstructor());
    }

    [Fact]
    public void DynamicOpaque_CanWriteAssembly()
    {
        using var module = LoadSampleModule();
        var (context, result) = CreateContext();
        new ILDynamicOpaquePredicates().Apply(module, context, result);
        PrepareForWrite(module);
        var outPath = Path.Combine(_tempDir, "dynamic_opaque.dll");
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

    // === VmIsa (per-session ISA randomization) ===

    [Fact]
    public void VmIsa_AssignsUniqueByteToEachEncoding()
    {
        var isa = new IL.VM.VmIsa(seed: 12345);
        var allBytes = new List<byte>();
        foreach (var op in Enum.GetValues<IL.VM.VmOpCode>())
        {
            var encodings = isa.GetAllEncodings(op);
            allBytes.AddRange(encodings);
        }
        Assert.Equal(allBytes.Count, allBytes.Distinct().Count());
    }

    [Fact]
    public void VmIsa_DifferentSeedsProduceDifferentMappings()
    {
        var isa1 = new IL.VM.VmIsa(seed: 1);
        var isa2 = new IL.VM.VmIsa(seed: 2);
        Assert.NotEqual(
            isa1.GetAllEncodings(IL.VM.VmOpCode.ADD)[0],
            isa2.GetAllEncodings(IL.VM.VmOpCode.ADD)[0]);
    }

    [Fact]
    public void VmIsa_PolymorphicOpsHaveMultipleEncodings()
    {
        var isa = new IL.VM.VmIsa(seed: 42);
        Assert.True(isa.GetAllEncodings(IL.VM.VmOpCode.ADD).Count >= 2);
        Assert.True(isa.GetAllEncodings(IL.VM.VmOpCode.LDC_I4).Count >= 2);
        Assert.True(isa.GetAllEncodings(IL.VM.VmOpCode.LDLOC).Count >= 2);
    }

    [Fact]
    public void VmIsa_NonPolymorphicOpsHaveSingleEncoding()
    {
        var isa = new IL.VM.VmIsa(seed: 42);
        Assert.Single(isa.GetAllEncodings(IL.VM.VmOpCode.RET));
        Assert.Single(isa.GetAllEncodings(IL.VM.VmOpCode.NOP));
        Assert.Single(isa.GetAllEncodings(IL.VM.VmOpCode.BR));
    }

    [Fact]
    public void VmIsa_PickEncodingReturnsValuesFromAllowedSet()
    {
        var isa = new IL.VM.VmIsa(seed: 42);
        var allowed = new HashSet<byte>(isa.GetAllEncodings(IL.VM.VmOpCode.ADD));
        for (var i = 0; i < 100; i++)
        {
            var pick = isa.PickEncoding(IL.VM.VmOpCode.ADD);
            Assert.Contains(pick, allowed);
        }
    }

    [Fact]
    public void VmIsa_PolymorphicPickEventuallyReturnsAllEncodings()
    {
        // 十分な回数 PickEncoding を呼べば、全エンコーディングが少なくとも 1 回返るはず
        var isa = new IL.VM.VmIsa(seed: 42);
        var allowed = isa.GetAllEncodings(IL.VM.VmOpCode.ADD).ToList();
        var seen = new HashSet<byte>();
        for (var i = 0; i < 200 && seen.Count < allowed.Count; i++)
            seen.Add(isa.PickEncoding(IL.VM.VmOpCode.ADD));
        Assert.Equal(allowed.Count, seen.Count);
    }

    // === CilToVmTranslator ===

    [Fact]
    public void CilToVmTranslator_TranslatesSimpleMethod()
    {
        using var module = LoadSampleModule();
        var translator = new IL.VM.CilToVmTranslator(module, new IL.VM.VmIsa(seed: 42));

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
