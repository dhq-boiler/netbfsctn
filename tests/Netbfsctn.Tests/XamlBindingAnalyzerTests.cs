using Netbfsctn.Core.Logging;
using Netbfsctn.Core.Xaml;

namespace Netbfsctn.Tests;

/// <summary>
/// XAML 解析が、リネームから守るべき名前を正しく拾うかを確認する。
/// </summary>
public class XamlBindingAnalyzerTests : IDisposable
{
    private readonly string _dir;

    public XamlBindingAnalyzerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netbfsctn_xaml_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void WriteXaml(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    [Fact]
    public void XStatic_EnumMember_IsProtectedAsEnumValue()
    {
        // {x:Static svc:AppTheme.System} の "System" は enum 値。BAML は値を文字列で焼き、
        // 起動時に Enum.Parse するため、フィールド名 (=値名) をリネームすると
        // XamlParseException ('System' was not found) で落ちる。値名を守れているか。
        WriteXaml("MainWindow.xaml", """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:svc="clr-namespace:PaperPlane.Services">
                <MenuItem CommandParameter="{x:Static svc:AppTheme.System}" />
                <MenuItem CommandParameter="{x:Static svc:AppTheme.Dark}" />
            </Window>
            """);

        var result = XamlBindingAnalyzer.Analyze([_dir], new ObfuscationLogger(verbose: false, quiet: true));

        Assert.Contains("System", result.ReferencedEnumValues);
        Assert.Contains("Dark", result.ReferencedEnumValues);
        // 型自体も参照として保護される
        Assert.Contains("PaperPlane.Services.AppTheme", result.ReferencedTypes);
    }
}
