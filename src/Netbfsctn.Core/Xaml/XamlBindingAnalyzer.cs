using System.Text.RegularExpressions;
using System.Xml.Linq;
using Netbfsctn.Core.Logging;

namespace Netbfsctn.Core.Xaml;

/// <summary>
/// XAML ファイルを解析し、データバインディング・型参照・イベントハンドラを抽出する。
/// 抽出された名前は名前難読化(リネーム)から除外される。
/// </summary>
public static partial class XamlBindingAnalyzer
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    // {Binding ...} または {x:Bind ...} のパス部分を抽出
    [GeneratedRegex(@"\{(?:Binding|x:Bind)\s+(?:Path\s*=\s*)?([^},]+)", RegexOptions.Compiled)]
    private static partial Regex BindingPathRegex();

    // {x:Type prefix:TypeName} を抽出
    [GeneratedRegex(@"\{x:Type\s+(\w+:\w+|\w+)\}", RegexOptions.Compiled)]
    private static partial Regex XTypeRegex();

    // {x:Static prefix:Type.Member} を抽出
    [GeneratedRegex(@"\{x:Static\s+(\w+:\w+\.\w+|\w+\.\w+)\}", RegexOptions.Compiled)]
    private static partial Regex XStaticRegex();

    // C# 識別子パターン
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex CSharpIdentifierRegex();

    // XAML の標準プロパティ属性名（イベントハンドラではないもの）
    private static readonly HashSet<string> NonEventAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Key", "Class", "Uid", "FieldModifier",
        "Content", "Text", "Title", "Header", "Tag", "ToolTip",
        "Source", "DataContext", "ItemsSource", "SelectedItem", "SelectedValue",
        "TargetType", "BasedOn", "DataType", "Style", "Template",
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "Margin", "Padding",
        "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
        "Orientation", "FlowDirection", "Visibility", "IsEnabled", "IsReadOnly",
        "FontFamily", "FontSize", "FontWeight", "FontStyle", "FontStretch",
        "Foreground", "Background", "BorderBrush", "BorderThickness", "Fill", "Stroke",
        "Opacity", "Cursor", "Stretch", "StrokeThickness",
        "Command", "CommandParameter", "CommandTarget",
        "Value", "Minimum", "Maximum",
        "ColumnDefinitions", "RowDefinitions", "CornerRadius",
        "ItemTemplate", "ItemContainerStyle", "ContentTemplate",
        "SelectedIndex", "SelectedValuePath", "DisplayMemberPath",
        "NavigateUri", "TargetName",
        "Converter", "ConverterParameter", "StringFormat", "FallbackValue",
        "Mode", "UpdateSourceTrigger", "RelativeSource", "ElementName",
        "x:Key", "x:Name", "x:Class", "x:Uid",
    };

    /// <summary>
    /// 指定ディレクトリ内の全 XAML ファイルを解析する。
    /// </summary>
    public static XamlAnalysisResult Analyze(string[] xamlDirectories, ObfuscationLogger logger)
    {
        var result = new XamlAnalysisResult();

        foreach (var dir in xamlDirectories)
        {
            if (!Directory.Exists(dir))
            {
                logger.Warning($"XAML ディレクトリが見つかりません: {dir}");
                continue;
            }

            var files = Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    AnalyzeFile(file, result, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning($"XAML 解析エラー ({file}): {ex.Message}");
                }
            }
        }

        return result;
    }

    private static void AnalyzeFile(string filePath, XamlAnalysisResult result, ObfuscationLogger logger)
    {
        var doc = XDocument.Load(filePath);
        if (doc.Root == null) return;

        // xmlns clr-namespace マッピングを収集 (prefix → namespace)
        var nsMappings = CollectNamespaceMappings(doc.Root);

        // 全要素を走査
        foreach (var element in doc.Root.DescendantsAndSelf())
        {
            // 要素名から型参照を抽出 (e.g., <local:MyControl>)
            ExtractTypeFromElementName(element, nsMappings, result);

            foreach (var attr in element.Attributes())
            {
                var localName = attr.Name.LocalName;
                var value = attr.Value;

                // x:Class → 型のフルネームをそのまま登録
                if (attr.Name.Namespace == XamlNs && localName == "Class")
                {
                    result.ReferencedTypes.Add(value);
                    logger.Verbose($"XAML x:Class: {value}");
                    continue;
                }

                // TargetType / DataType に含まれる型参照
                if (localName is "TargetType" or "DataType")
                {
                    ExtractTypeReferences(value, nsMappings, result, logger);
                    continue;
                }

                // 属性値内の Binding パス
                ExtractBindingPaths(value, result, logger);

                // 属性値内の x:Type 参照
                ExtractXTypeReferences(value, nsMappings, result, logger);

                // 属性値内の x:Static 参照
                ExtractXStaticReferences(value, nsMappings, result, logger);

                // イベントハンドラ候補および enum 値候補の検出
                ExtractEventHandlerOrEnumValue(attr, result, logger);
            }
        }
    }

    /// <summary>
    /// ルート要素から xmlns:prefix="clr-namespace:..." 宣言を収集する。
    /// </summary>
    private static Dictionary<string, string> CollectNamespaceMappings(XElement root)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attr in root.Attributes())
        {
            if (attr.Name.Namespace != XNamespace.Xmlns) continue;

            var prefix = attr.Name.LocalName;
            var nsValue = attr.Value;

            // clr-namespace:Namespace or clr-namespace:Namespace;assembly=...
            if (nsValue.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                var ns = nsValue["clr-namespace:".Length..];
                var semicolonIdx = ns.IndexOf(';');
                if (semicolonIdx >= 0)
                    ns = ns[..semicolonIdx];

                mappings[prefix] = ns;
            }
        }

        return mappings;
    }

    /// <summary>
    /// 要素名から型参照を抽出する (e.g., &lt;local:MyControl&gt; → namespace.MyControl)。
    /// </summary>
    private static void ExtractTypeFromElementName(
        XElement element, Dictionary<string, string> nsMappings, XamlAnalysisResult result)
    {
        // 要素の名前空間が clr-namespace にマッピングされているか
        var elementNs = element.Name.NamespaceName;
        if (elementNs.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var ns = elementNs["clr-namespace:".Length..];
            var semicolonIdx = ns.IndexOf(';');
            if (semicolonIdx >= 0)
                ns = ns[..semicolonIdx];

            var fullName = $"{ns}.{element.Name.LocalName}";
            result.ReferencedTypes.Add(fullName);
        }
    }

    /// <summary>
    /// {Binding ...} パスからプロパティ名を抽出する。
    /// パスのセグメント (Prop1.Prop2[0].Prop3) をすべてプロパティ名として登録する。
    /// </summary>
    private static void ExtractBindingPaths(string value, XamlAnalysisResult result, ObfuscationLogger logger)
    {
        foreach (Match match in BindingPathRegex().Matches(value))
        {
            var path = match.Groups[1].Value.Trim();

            // Source=, Converter= 等のサブプロパティ指定を除外
            if (path.Contains('=')) continue;

            // パスを . で分割し、各セグメントのプロパティ名を抽出
            var segments = path.Split('.');
            foreach (var segment in segments)
            {
                // インデクサ [0] を除去
                var propName = segment;
                var bracketIdx = propName.IndexOf('[');
                if (bracketIdx >= 0)
                    propName = propName[..bracketIdx];

                propName = propName.Trim();
                if (propName.Length > 0 && CSharpIdentifierRegex().IsMatch(propName))
                {
                    result.BoundPropertyNames.Add(propName);
                    logger.Verbose($"XAML Binding プロパティ: {propName}");
                }
            }
        }
    }

    /// <summary>
    /// {x:Type prefix:TypeName} から型名を解決して登録する。
    /// </summary>
    private static void ExtractXTypeReferences(
        string value, Dictionary<string, string> nsMappings,
        XamlAnalysisResult result, ObfuscationLogger logger)
    {
        foreach (Match match in XTypeRegex().Matches(value))
        {
            var typeRef = match.Groups[1].Value;
            var fullName = ResolveTypeReference(typeRef, nsMappings);
            if (fullName != null)
            {
                result.ReferencedTypes.Add(fullName);
                logger.Verbose($"XAML x:Type: {fullName}");
            }
        }
    }

    /// <summary>
    /// TargetType, DataType 属性値から型参照を抽出する。
    /// {x:Type ...} 形式と直接型名の両方に対応。
    /// </summary>
    private static void ExtractTypeReferences(
        string value, Dictionary<string, string> nsMappings,
        XamlAnalysisResult result, ObfuscationLogger logger)
    {
        // {x:Type prefix:TypeName} 形式
        var xTypeMatch = XTypeRegex().Match(value);
        if (xTypeMatch.Success)
        {
            var fullName = ResolveTypeReference(xTypeMatch.Groups[1].Value, nsMappings);
            if (fullName != null)
            {
                result.ReferencedTypes.Add(fullName);
                logger.Verbose($"XAML TargetType/DataType: {fullName}");
            }
            return;
        }

        // 直接型名 (prefix:TypeName) 形式
        if (value.Contains(':'))
        {
            var fullName = ResolveTypeReference(value.Trim(), nsMappings);
            if (fullName != null)
            {
                result.ReferencedTypes.Add(fullName);
                logger.Verbose($"XAML TargetType/DataType (直接): {fullName}");
            }
        }
    }

    /// <summary>
    /// {x:Static prefix:Type.Member} から型名とメンバー名を抽出する。
    /// </summary>
    private static void ExtractXStaticReferences(
        string value, Dictionary<string, string> nsMappings,
        XamlAnalysisResult result, ObfuscationLogger logger)
    {
        foreach (Match match in XStaticRegex().Matches(value))
        {
            var staticRef = match.Groups[1].Value;
            var dotIdx = staticRef.LastIndexOf('.');
            if (dotIdx < 0) continue;

            var typeRef = staticRef[..dotIdx];
            var memberName = staticRef[(dotIdx + 1)..];

            var fullTypeName = ResolveTypeReference(typeRef, nsMappings);
            if (fullTypeName != null)
            {
                result.ReferencedTypes.Add(fullTypeName);
                result.BoundPropertyNames.Add(memberName);
                logger.Verbose($"XAML x:Static: {fullTypeName}.{memberName}");
            }
        }
    }

    /// <summary>
    /// イベントハンドラ候補および enum 値候補を検出する。
    /// XML 名前空間プレフィックスなし、値が C# 識別子パターンの属性値を収集する。
    /// </summary>
    private static void ExtractEventHandlerOrEnumValue(XAttribute attr, XamlAnalysisResult result, ObfuscationLogger logger)
    {
        // XML 名前空間付き属性はスキップ (x:Key, d:DesignWidth 等)
        if (attr.Name.Namespace != XNamespace.None) return;

        var localName = attr.Name.LocalName;
        var value = attr.Value;

        // マークアップ拡張 ({Binding ...} 等) はスキップ
        if (value.StartsWith('{')) return;

        // 添付プロパティ (Grid.Row 等) はスキップ
        if (localName.Contains('.')) return;

        // 値が C# 識別子パターンに一致するか
        if (!CSharpIdentifierRegex().IsMatch(value)) return;

        // True/False はリネーム対象外
        if (value is "True" or "False") return;

        // 既知の非イベント属性名なら enum 値候補として登録
        if (NonEventAttributes.Contains(localName))
        {
            result.ReferencedEnumValues.Add(value);
            logger.Verbose($"XAML enum 値候補: {localName}=\"{value}\"");
            return;
        }

        // それ以外の属性: イベントハンドラか enum 値か判別が難しいため両方に登録
        result.EventHandlerNames.Add(value);
        result.ReferencedEnumValues.Add(value);
        logger.Verbose($"XAML イベントハンドラ/enum 候補: {localName}=\"{value}\"");
    }

    /// <summary>
    /// prefix:TypeName を namespace.TypeName に解決する。
    /// </summary>
    private static string? ResolveTypeReference(string typeRef, Dictionary<string, string> nsMappings)
    {
        var colonIdx = typeRef.IndexOf(':');
        if (colonIdx < 0) return null; // プレフィックスなし = WPF 標準型 → 対象外

        var prefix = typeRef[..colonIdx];
        var typeName = typeRef[(colonIdx + 1)..];

        if (nsMappings.TryGetValue(prefix, out var ns))
            return $"{ns}.{typeName}";

        return null;
    }

}
