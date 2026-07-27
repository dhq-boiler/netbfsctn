using Netbfsctn.Core.Logging;
using Netbfsctn.Core.NameGeneration;
using Netbfsctn.Core.Xaml;

namespace Netbfsctn.Core.Pipeline;

public class ObfuscationContext
{
    public required ObfuscationOptions Options { get; init; }
    public required ObfuscationLogger Logger { get; init; }
    public ConfusableNameGenerator NameGenerator { get; } = new();
    public Dictionary<string, string> NameMap { get; } = new();

    /// <summary>
    /// メンバーリネーム履歴: (TypeDef のオブジェクト参照, 元のメンバー名, パラメータ数) → 新しいメンバー名。
    /// ILNameObfuscator が記録し、パイプラインのクロスアセンブリ参照修正フェーズで使用する。
    /// TypeDef はリネーム後も同一オブジェクトなので、オブジェクト参照で安全にルックアップできる。
    /// パラメータ数で区別することで、同名メソッドのオーバーロード (例: Dispose() と Dispose(bool)) を正しく処理する。
    /// フィールドは -1 を使用。
    /// </summary>
    public Dictionary<(object TypeDef, string OldMemberName, int ParamCount), string> MemberRenameHistory { get; } = new();

    /// <summary>
    /// public リネームから除外するモジュール名のセット。
    /// WPF自動検出 および --exclude-rename-public で指定されたアセンブリが格納される。
    /// EnableRenamePublic 有効時、ここに含まれないモジュールが public リネーム対象となる。
    /// </summary>
    public HashSet<string> ExcludeRenamePublicModules { get; } = new();

    /// <summary>
    /// 与えられた型が rename 除外対象かを返す。
    ///
    /// 名前空間はプレフィックス一致 (<c>PaperPlane.Core.Models</c> 指定で、その名前空間と
    /// 配下 (<c>...Models.Foo</c>) の型をすべて対象にする)。型 FullName は完全一致。
    /// どちらの指定も無いときは常に false で、判定コストもかからない。
    ///
    /// ネスト型は呼び出し側で最外殻の名前空間を渡すこと (ネスト型自身の Namespace は空になる)。
    /// </summary>
    public bool IsRenameExcluded(string typeNamespace, string typeFullName)
    {
        if (Options.NoRenameTypes.Length == 0 && Options.NoRenameNamespaces.Length == 0)
            return false;

        foreach (var fqn in Options.NoRenameTypes)
        {
            if (string.Equals(typeFullName, fqn, StringComparison.Ordinal))
                return true;
        }

        foreach (var ns in Options.NoRenameNamespaces)
        {
            if (string.IsNullOrEmpty(ns)) continue;
            if (string.Equals(typeNamespace, ns, StringComparison.Ordinal))
                return true;
            if (typeNamespace.StartsWith(ns + ".", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// XAML 解析結果。バインディングで参照されるプロパティ名・型名・イベントハンドラ名を保持する。
    /// null の場合、XAML 解析は無効 (--xaml-dir 未指定)。
    /// </summary>
    public XamlAnalysisResult? XamlAnalysis { get; set; }

    /// <summary>
    /// WPF アセンブリとして検出されたモジュール名のセット。
    /// WPF アセンブリでは BAML の型テーブルとの整合性のため、型名リネームを自動スキップする。
    /// </summary>
    public HashSet<string> WpfModuleNames { get; } = new();

    /// <summary>
    /// 同一バッチで処理中の全モジュールのアセンブリ名セット。
    /// メイン入力 + --additional-input で指定された全アセンブリ。
    /// クロスアセンブリの暗黙インターフェース実装で、iface 側と impl 側が別モジュールに分かれている場合に、
    /// 「外部だが同じバッチに居る」インターフェイスを検出するために使う。バッチ内 iface は
    /// 名前リネームをスキップせず、MemberRenameHistory を媒介に impl と同一の新名へ揃える。
    /// </summary>
    public HashSet<string> BatchAssemblyNames { get; } = new();

    /// <summary>
    /// バッチ内の全モジュールの型を、リネーム前の元 FullName → TypeDef オブジェクト参照 で引ける辞書。
    /// リネーム開始前 (Phase 2a 完了時点) に構築されるため、キーは常に元の FullName。
    /// 値は TypeDef オブジェクト参照で、リネーム後も同一オブジェクトなので追跡できる。
    ///
    /// 用途: クロスアセンブリの暗黙インターフェース実装検出で、impl 側から iface 側の
    /// TypeDef を探す際、dnlib の Resolve() が (probing path の関係で) 失敗するケースに
    /// 対する fallback として使用。例: MathIa.dll が PackageApi.IGeneratorPackage を実装
    /// する場合、MathIa の隣に PackageApi.dll が置かれていないと Resolve は null を返すが、
    /// この辞書ならバッチ内なので必ず引ける。
    ///
    /// 型は <c>object</c> にしておく (実体は <c>dnlib.DotNet.TypeDef</c> だが、Core が dnlib
    /// に依存しないよう抽象化)。IL 側でキャストして使用する。
    /// </summary>
    public Dictionary<string, object> BatchTypesByOriginalFullName { get; } = new(StringComparer.Ordinal);
}
