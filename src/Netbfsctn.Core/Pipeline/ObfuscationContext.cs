using Netbfsctn.Core.Logging;
using Netbfsctn.Core.NameGeneration;

namespace Netbfsctn.Core.Pipeline;

public class ObfuscationContext
{
    public required ObfuscationOptions Options { get; init; }
    public required ObfuscationLogger Logger { get; init; }
    public ConfusableNameGenerator NameGenerator { get; } = new();
    public Dictionary<string, string> NameMap { get; } = new();

    /// <summary>
    /// メンバーリネーム履歴: (TypeDef のオブジェクト参照, 元のメンバー名) → 新しいメンバー名。
    /// ILNameObfuscator が記録し、パイプラインのクロスアセンブリ参照修正フェーズで使用する。
    /// TypeDef はリネーム後も同一オブジェクトなので、オブジェクト参照で安全にルックアップできる。
    /// </summary>
    public Dictionary<(object TypeDef, string OldMemberName), string> MemberRenameHistory { get; } = new();

    /// <summary>
    /// public リネームから除外するモジュール名のセット。
    /// WPF自動検出 および --exclude-rename-public で指定されたアセンブリが格納される。
    /// EnableRenamePublic 有効時、ここに含まれないモジュールが public リネーム対象となる。
    /// </summary>
    public HashSet<string> ExcludeRenamePublicModules { get; } = new();
}
