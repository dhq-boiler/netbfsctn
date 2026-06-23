using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.IL;

namespace Netbfsctn.Tests;

/// <summary>
/// クロスアセンブリの暗黙インターフェース実装 (interface 側と impl 側が別 dll に分かれる
/// シナリオ) で、--rename-public 時に両側のメンバー名が同じ新名に揃うことを検証する。
///
/// 揃わないと CLR の interface dispatch がスロットを解決できず TypeLoadException
/// ("Method 'X' in type 'Y' does not have an implementation") になる。
/// </summary>
public class CrossAssemblyInterfaceTests : IDisposable
{
    private readonly string _tempDir;

    public CrossAssemblyInterfaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "netbfsctn_xasm_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RenamePublic_CrossAssemblyInterfaceMethod_NamesMatch()
    {
        var (apiPath, implPath) = BuildSampleAssemblies();

        var apiOut = Path.Combine(_tempDir, "Api.obf.dll");
        var implOut = Path.Combine(_tempDir, "Impl.obf.dll");

        // メイン入力に impl を、追加入力に iface を渡す (ユーザー報告のコマンドと同じ順序)
        var options = new ObfuscationOptions
        {
            InputPath = implPath,
            OutputPath = implOut,
            AdditionalInputPaths = [apiPath],
            AdditionalOutputPaths = [apiOut],
            EnableRename = true,
            EnableRenamePublic = true,
            Verbose = true,
        };

        var logger = new ObfuscationLogger(verbose: false, quiet: true);
        var context = new ObfuscationContext { Options = options, Logger = logger };

        var result = new ILObfuscationPipeline().Execute(context);
        Assert.True(result.Success, $"Pipeline failed: {result.ErrorMessage}");

        using var apiOutMod = ModuleDefMD.Load(apiOut);
        using var implOutMod = ModuleDefMD.Load(implOut);

        var ifaceType = apiOutMod.GetTypes().FirstOrDefault(t => t.IsInterface);
        Assert.NotNull(ifaceType);
        var ifaceMethod = ifaceType!.Methods.FirstOrDefault(m =>
            m.IsVirtual && m.IsAbstract && !m.IsConstructor);
        Assert.NotNull(ifaceMethod);

        var implType = implOutMod.GetTypes().FirstOrDefault(t =>
            !t.IsInterface && t.Interfaces.Count > 0);
        Assert.NotNull(implType);
        var implMethod = implType!.Methods.FirstOrDefault(m =>
            m.IsVirtual && !m.IsConstructor &&
            m.MethodSig?.Params.Count == 0 &&
            m.MethodSig?.RetType.FullName == apiOutMod.CorLibTypes.String.FullName);
        Assert.NotNull(implMethod);

        Assert.Equal(ifaceMethod.Name.String, implMethod.Name.String);
        // public method なのでリネームされていること (元の名前ではないこと)
        Assert.NotEqual("GetName", implMethod.Name.String);
    }

    [Fact]
    public void RenamePublic_CrossAssemblyInterface_OnWpfImplModule_NamesMatch()
    {
        // ユーザー報告のシナリオ: impl 側 (SPI3Trainer) が WPF アセンブリで、
        // iface 側 (PackageApi) は通常アセンブリ。--xaml-dir 指定で WPF 除外を外しても
        // RenameVirtualPropertyGroups がモジュール全体をスキップして impl 側が未リネームのまま、
        // iface 側だけリネームされて CLR の暗黙インターフェース dispatch が崩れていた。
        var (apiPath, implPath) = BuildSampleAssembliesWithProperty(wpfImpl: true);

        var xamlDir = Path.Combine(_tempDir, "xaml-empty");
        Directory.CreateDirectory(xamlDir);

        var apiOut = Path.Combine(_tempDir, "ApiWpf.obf.dll");
        var implOut = Path.Combine(_tempDir, "ImplWpf.obf.dll");

        var options = new ObfuscationOptions
        {
            InputPath = implPath,
            OutputPath = implOut,
            AdditionalInputPaths = [apiPath],
            AdditionalOutputPaths = [apiOut],
            EnableRename = true,
            EnableRenamePublic = true,
            XamlDirectories = [xamlDir],
            Verbose = true,
        };

        var logger = new ObfuscationLogger(verbose: false, quiet: true);
        var context = new ObfuscationContext { Options = options, Logger = logger };

        var result = new ILObfuscationPipeline().Execute(context);
        Assert.True(result.Success, $"Pipeline failed: {result.ErrorMessage}");

        using var apiOutMod = ModuleDefMD.Load(apiOut);
        using var implOutMod = ModuleDefMD.Load(implOut);

        var ifaceType = apiOutMod.GetTypes().FirstOrDefault(t => t.IsInterface);
        Assert.NotNull(ifaceType);
        var ifaceGetter = ifaceType!.Methods.FirstOrDefault(m => m.IsGetter);
        Assert.NotNull(ifaceGetter);

        var implType = implOutMod.GetTypes().FirstOrDefault(t =>
            !t.IsInterface && t.Interfaces.Count > 0);
        Assert.NotNull(implType);
        var implGetter = implType!.Methods.FirstOrDefault(m => m.IsGetter);
        Assert.NotNull(implGetter);

        Assert.Equal(ifaceGetter.Name.String, implGetter.Name.String);
        Assert.NotEqual("get_Id", implGetter.Name.String);
    }

    [Fact]
    public void RenamePublic_CrossAssemblyInterfaceProperty_NamesMatch()
    {
        var (apiPath, implPath) = BuildSampleAssembliesWithProperty();

        var apiOut = Path.Combine(_tempDir, "ApiP.obf.dll");
        var implOut = Path.Combine(_tempDir, "ImplP.obf.dll");

        var options = new ObfuscationOptions
        {
            InputPath = implPath,
            OutputPath = implOut,
            AdditionalInputPaths = [apiPath],
            AdditionalOutputPaths = [apiOut],
            EnableRename = true,
            EnableRenamePublic = true,
            Verbose = true,
        };

        var logger = new ObfuscationLogger(verbose: false, quiet: true);
        var context = new ObfuscationContext { Options = options, Logger = logger };

        var result = new ILObfuscationPipeline().Execute(context);
        Assert.True(result.Success, $"Pipeline failed: {result.ErrorMessage}");

        using var apiOutMod = ModuleDefMD.Load(apiOut);
        using var implOutMod = ModuleDefMD.Load(implOut);

        var ifaceType = apiOutMod.GetTypes().FirstOrDefault(t => t.IsInterface);
        Assert.NotNull(ifaceType);
        var ifaceGetter = ifaceType!.Methods.FirstOrDefault(m => m.IsGetter);
        Assert.NotNull(ifaceGetter);

        var implType = implOutMod.GetTypes().FirstOrDefault(t =>
            !t.IsInterface && t.Interfaces.Count > 0);
        Assert.NotNull(implType);
        var implGetter = implType!.Methods.FirstOrDefault(m => m.IsGetter);
        Assert.NotNull(implGetter);

        Assert.Equal(ifaceGetter.Name.String, implGetter.Name.String);
        Assert.NotEqual("get_Id", implGetter.Name.String);
    }

    // --- ヘルパー: dnlib で最小サンプルアセンブリを構築 ---

    private (string apiPath, string implPath) BuildSampleAssemblies()
    {
        var corLibRef = new AssemblyRefUser(typeof(object).Assembly.GetName().Name!,
            typeof(object).Assembly.GetName().Version!);

        // ── Api.dll ──
        var apiMod = new ModuleDefUser("Api.dll", Guid.NewGuid(), corLibRef)
        {
            Kind = ModuleKind.Dll,
            RuntimeVersion = MDHeaderRuntimeVersion.MS_CLR_40,
        };
        var apiAsm = new AssemblyDefUser("Api", new Version(1, 0, 0, 0));
        apiAsm.Modules.Add(apiMod);

        var iface = new TypeDefUser("Api", "IPackage", apiMod.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Interface
                | TypeAttributes.Abstract,
        };
        var getName = new MethodDefUser("GetName",
            MethodSig.CreateInstance(apiMod.CorLibTypes.String),
            MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot
            | MethodAttributes.Abstract | MethodAttributes.Virtual);
        iface.Methods.Add(getName);
        apiMod.Types.Add(iface);

        var apiPath = Path.Combine(_tempDir, "Api.dll");
        apiMod.Write(apiPath);
        apiMod.Dispose();

        // ── Impl.dll (Api.dll を参照) ──
        // 共有 ModuleContext を作って Impl 側から Api 側を解決できるようにする
        var resolver = new AssemblyResolver();
        var modCtx = new ModuleContext(resolver);
        resolver.DefaultModuleContext = modCtx;
        resolver.PreSearchPaths.Add(_tempDir);

        var implMod = new ModuleDefUser("Impl.dll", Guid.NewGuid(), corLibRef)
        {
            Kind = ModuleKind.Dll,
            RuntimeVersion = MDHeaderRuntimeVersion.MS_CLR_40,
            Context = modCtx,
        };
        var implAsm = new AssemblyDefUser("Impl", new Version(1, 0, 0, 0));
        implAsm.Modules.Add(implMod);

        var apiRef = implMod.UpdateRowId(new AssemblyRefUser("Api", new Version(1, 0, 0, 0)));
        var ifaceRef = new TypeRefUser(implMod, "Api", "IPackage", apiRef);

        var implClass = new TypeDefUser("Impl", "Package", implMod.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class
                | TypeAttributes.BeforeFieldInit,
        };
        implClass.Interfaces.Add(new InterfaceImplUser(ifaceRef));

        var objCtor = new MemberRefUser(implMod, ".ctor",
            MethodSig.CreateInstance(implMod.CorLibTypes.Void),
            implMod.CorLibTypes.Object.TypeDefOrRef);
        var ctor = new MethodDefUser(".ctor",
            MethodSig.CreateInstance(implMod.CorLibTypes.Void),
            MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        ctor.Body.Instructions.Add(OpCodes.Call.ToInstruction(objCtor));
        ctor.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        implClass.Methods.Add(ctor);

        // C# の暗黙インターフェース実装と同じ属性: public virtual final newslot
        var implGetName = new MethodDefUser("GetName",
            MethodSig.CreateInstance(implMod.CorLibTypes.String),
            MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot
            | MethodAttributes.Final | MethodAttributes.Virtual);
        implGetName.Body = new CilBody();
        implGetName.Body.Instructions.Add(OpCodes.Ldstr.ToInstruction("hello"));
        implGetName.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        implClass.Methods.Add(implGetName);

        implMod.Types.Add(implClass);

        var implPath = Path.Combine(_tempDir, "Impl.dll");
        implMod.Write(implPath);
        implMod.Dispose();

        return (apiPath, implPath);
    }

    private (string apiPath, string implPath) BuildSampleAssembliesWithProperty(bool wpfImpl = false)
    {
        var corLibRef = new AssemblyRefUser(typeof(object).Assembly.GetName().Name!,
            typeof(object).Assembly.GetName().Version!);

        var prefix = wpfImpl ? "ApiWpf" : "ApiP";
        var implPrefix = wpfImpl ? "ImplWpf" : "ImplP";

        // ── Api.dll ── (interface IPackage { string Id { get; } })
        var apiMod = new ModuleDefUser($"{prefix}.dll", Guid.NewGuid(), corLibRef)
        {
            Kind = ModuleKind.Dll,
            RuntimeVersion = MDHeaderRuntimeVersion.MS_CLR_40,
        };
        var apiAsm = new AssemblyDefUser(prefix, new Version(1, 0, 0, 0));
        apiAsm.Modules.Add(apiMod);

        var iface = new TypeDefUser("Api", "IPackage", apiMod.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Interface
                | TypeAttributes.Abstract,
        };
        var ifaceGetter = new MethodDefUser("get_Id",
            MethodSig.CreateInstance(apiMod.CorLibTypes.String),
            MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot
            | MethodAttributes.SpecialName | MethodAttributes.Abstract | MethodAttributes.Virtual);
        iface.Methods.Add(ifaceGetter);
        var ifaceProp = new PropertyDefUser("Id",
            new PropertySig(true, apiMod.CorLibTypes.String));
        ifaceProp.GetMethod = ifaceGetter;
        iface.Properties.Add(ifaceProp);
        apiMod.Types.Add(iface);

        var apiPath = Path.Combine(_tempDir, $"{prefix}.dll");
        apiMod.Write(apiPath);
        apiMod.Dispose();

        // ── Impl.dll ── (sealed class Package : IPackage { public string Id => "x"; })
        var resolver = new AssemblyResolver();
        var modCtx = new ModuleContext(resolver);
        resolver.DefaultModuleContext = modCtx;
        resolver.PreSearchPaths.Add(_tempDir);

        var implMod = new ModuleDefUser($"{implPrefix}.dll", Guid.NewGuid(), corLibRef)
        {
            Kind = ModuleKind.Dll,
            RuntimeVersion = MDHeaderRuntimeVersion.MS_CLR_40,
            Context = modCtx,
        };
        var implAsm = new AssemblyDefUser(implPrefix, new Version(1, 0, 0, 0));
        implAsm.Modules.Add(implMod);

        // WPF アセンブリ判定用に PresentationFramework への参照を追加
        // (IsWpfAssembly が PresentationFramework / PresentationCore 参照で判定する)
        if (wpfImpl)
        {
            implMod.UpdateRowId(new AssemblyRefUser("PresentationFramework", new Version(10, 0, 0, 0)));
        }

        var apiRef = implMod.UpdateRowId(new AssemblyRefUser(prefix, new Version(1, 0, 0, 0)));
        var ifaceRef = new TypeRefUser(implMod, "Api", "IPackage", apiRef);

        var implClass = new TypeDefUser("Impl", "Package",
            implMod.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class
                | TypeAttributes.BeforeFieldInit,
        };
        implClass.Interfaces.Add(new InterfaceImplUser(ifaceRef));

        var objCtor = new MemberRefUser(implMod, ".ctor",
            MethodSig.CreateInstance(implMod.CorLibTypes.Void),
            implMod.CorLibTypes.Object.TypeDefOrRef);
        var ctor = new MethodDefUser(".ctor",
            MethodSig.CreateInstance(implMod.CorLibTypes.Void),
            MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        ctor.Body.Instructions.Add(OpCodes.Call.ToInstruction(objCtor));
        ctor.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        implClass.Methods.Add(ctor);

        var implGetter = new MethodDefUser("get_Id",
            MethodSig.CreateInstance(implMod.CorLibTypes.String),
            MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot
            | MethodAttributes.Final | MethodAttributes.SpecialName | MethodAttributes.Virtual);
        implGetter.Body = new CilBody();
        implGetter.Body.Instructions.Add(OpCodes.Ldstr.ToInstruction("x"));
        implGetter.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        implClass.Methods.Add(implGetter);

        var implProp = new PropertyDefUser("Id",
            new PropertySig(true, implMod.CorLibTypes.String));
        implProp.GetMethod = implGetter;
        implClass.Properties.Add(implProp);

        implMod.Types.Add(implClass);

        var implPath = Path.Combine(_tempDir, $"{implPrefix}.dll");
        implMod.Write(implPath);
        implMod.Dispose();

        return (apiPath, implPath);
    }
}
