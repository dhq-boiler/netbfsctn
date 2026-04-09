namespace Netbfsctn.Core.Xaml;

/// <summary>
/// XAML ファイル解析結果。バインディング・型参照・イベントハンドラ名を保持し、
/// リネーム時の除外判定に使用する。
/// </summary>
public class XamlAnalysisResult
{
    /// <summary>
    /// XAML から参照される型のフルネーム (namespace.TypeName)。
    /// x:Class, x:Type, DataType, TargetType, 要素名 (xmlns prefix 経由) 等から収集。
    /// </summary>
    public HashSet<string> ReferencedTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// バインディングパスで使用されるプロパティ名。
    /// {Binding PropertyName}, {Binding Path=Prop1.Prop2} 等から収集。
    /// 型を特定できないため、グローバルに名前で照合する。
    /// </summary>
    public HashSet<string> BoundPropertyNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// XAML のイベントハンドラとして参照されるメソッド名。
    /// </summary>
    public HashSet<string> EventHandlerNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// XAML 属性値として使用される識別子（enum 値候補）。
    /// BAML が enum 値を文字列で保存するため、該当する enum フィールド名をリネームから保護する。
    /// </summary>
    public HashSet<string> ReferencedEnumValues { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 解析対象外だった理由でスキップされたプロパティ名や型名は含まない。
    /// 結果が空の場合、XAML 解析が無効（ディレクトリ未指定）であることを示す。
    /// </summary>
    public bool IsEmpty => ReferencedTypes.Count == 0
        && BoundPropertyNames.Count == 0
        && EventHandlerNames.Count == 0
        && ReferencedEnumValues.Count == 0;
}
