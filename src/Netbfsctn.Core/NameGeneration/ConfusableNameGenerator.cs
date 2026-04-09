namespace Netbfsctn.Core.NameGeneration;

public class ConfusableNameGenerator
{
    // 視覚的に紛らわしい文字（l/I/O/0/o）
    private static readonly char[] ConfusableChars = ['l', 'I', 'O', '0', 'o'];

    private readonly Random _random;
    private readonly HashSet<string> _generated = new();
    private int _minAlphaCount = 3;

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

        // 衝突が多い場合はアルファベット文字数を増やす
        _minAlphaCount++;
        return Next();
    }

    /// <summary>
    /// アンダースコアの海にアルファベットが散在する名前を生成する。
    /// 各アルファベット文字の間に 2〜6 個のアンダースコアを挟み、
    /// その数を微妙にずらすことで、人間には「ほぼ同じに見える」が
    /// 実際には異なる名前を大量に生成する。
    ///
    /// 例: ___o__O______0___I, ____o__O_____0____I, ___o___O______0___I
    /// </summary>
    private string GenerateCandidate()
    {
        var alphaCount = _minAlphaCount + _random.Next(3);
        var chars = new List<char>();

        // 先頭アンダースコア (1〜6個)
        AppendUnderscores(chars, 1 + _random.Next(6));

        // 最初のアルファベット文字 (数字以外)
        chars.Add(PickNonDigit());

        // 残りのアルファベット文字 (間にアンダースコア)
        for (var i = 1; i < alphaCount; i++)
        {
            // 各文字間にアンダースコア (2〜6個)
            AppendUnderscores(chars, 2 + _random.Next(5));
            chars.Add(ConfusableChars[_random.Next(ConfusableChars.Length)]);
        }

        // 末尾アンダースコア (0〜3個, 50%の確率で付与)
        if (_random.Next(2) == 0)
            AppendUnderscores(chars, 1 + _random.Next(3));

        return new string(chars.ToArray());
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
