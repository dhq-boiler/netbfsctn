namespace Netbfsctn.Core.NameGeneration;

public class ConfusableNameGenerator
{
    // 視覚的に紛らわしい文字（l/I/O/0/o）
    private static readonly char[] ConfusableChars = ['l', 'I', 'O', '0', 'o'];

    private readonly Random _random;
    private readonly HashSet<string> _generated = new();
    private int _minAlphaCount = 3;

    /// <summary>
    /// アルファベットパターンのテンプレートプール。
    /// 同じ文字列パターンを使い回し、アンダースコアの数だけで区別させる。
    /// </summary>
    private readonly List<char[]> _alphaPatterns = new();

    public ConfusableNameGenerator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public string Next()
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var name = GenerateCandidate();
            if (_generated.Add(name))
                return name;
        }

        _minAlphaCount++;
        return Next();
    }

    /// <summary>
    /// アルファベットの並びは少数のパターンを使い回し、
    /// アンダースコアの数だけを微妙にずらすことで名前を生成する。
    /// 人間にはアルファベット部分が同じに見えるため、
    /// 「全部同じ名前に見える」状態を作り出す。
    ///
    /// 例: __o___I__l___O, ___o__I___l__O, __o____I__l__O
    /// </summary>
    private string GenerateCandidate()
    {
        var alphaCount = _minAlphaCount + _random.Next(3);

        // アルファベットパターンを取得（同じ文字数のパターンを再利用）
        var pattern = GetOrCreateAlphaPattern(alphaCount);

        var chars = new List<char>();

        // 先頭アンダースコア (1〜6個)
        AppendUnderscores(chars, 1 + _random.Next(6));

        // 最初のアルファベット文字
        chars.Add(pattern[0]);

        // 残りのアルファベット文字 (間にアンダースコア 2〜6個)
        for (var i = 1; i < alphaCount; i++)
        {
            AppendUnderscores(chars, 2 + _random.Next(5));
            chars.Add(pattern[i]);
        }

        // 末尾アンダースコア (0〜3個, 50%の確率で付与)
        if (_random.Next(2) == 0)
            AppendUnderscores(chars, 1 + _random.Next(3));

        return new string(chars.ToArray());
    }

    /// <summary>
    /// 指定文字数のアルファベットパターンを取得する。
    /// 同じ文字数に対して少数のパターン（最大3つ）を使い回す。
    /// </summary>
    private char[] GetOrCreateAlphaPattern(int length)
    {
        // 同じ長さのパターンを探す
        var candidates = new List<char[]>();
        foreach (var p in _alphaPatterns)
        {
            if (p.Length == length)
                candidates.Add(p);
        }

        // パターンが3つ未満なら80%の確率で新規作成
        if (candidates.Count < 3 && (candidates.Count == 0 || _random.Next(5) < 4))
        {
            var pattern = new char[length];
            pattern[0] = PickNonDigit();
            for (var i = 1; i < length; i++)
                pattern[i] = ConfusableChars[_random.Next(ConfusableChars.Length)];
            _alphaPatterns.Add(pattern);
            return pattern;
        }

        // 既存パターンからランダムに選択
        return candidates[_random.Next(candidates.Count)];
    }

    private static void AppendUnderscores(List<char> chars, int count)
    {
        for (var i = 0; i < count; i++)
            chars.Add('_');
    }

    private char PickNonDigit()
    {
        char c;
        do
        {
            c = ConfusableChars[_random.Next(ConfusableChars.Length)];
        } while (c is '0');

        return c;
    }
}
