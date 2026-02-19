namespace Netbfsctn.Core.NameGeneration;

public class ConfusableNameGenerator
{
    private static readonly char[] ConfusableChars = ['l', 'I', 'O', '0', 'o'];

    private readonly Random _random;
    private readonly HashSet<string> _generated = new();
    private int _minLength = 4;

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

        // 衝突が多い場合は長さを増やす
        _minLength++;
        return Next();
    }

    private string GenerateCandidate()
    {
        // 先頭にランダムな長さ（0〜10個）のアンダースコアを付加
        var prefixLen = _random.Next(11);
        var bodyLen = _minLength + _random.Next(3);

        var parts = new List<char>();

        // アンダースコアプレフィックス
        for (var i = 0; i < prefixLen; i++)
            parts.Add('_');

        // 本体の最初の文字は数字以外にする
        parts.Add(PickNonDigit());

        // 残りの本体文字を生成（途中にアンダースコアをランダム挿入）
        for (var i = 1; i < bodyLen; i++)
        {
            // 約25%の確率でアンダースコア連続を挿入
            if (_random.Next(4) == 0)
            {
                // 3〜8個の連続アンダースコア
                var runLen = 3 + _random.Next(6);
                for (var j = 0; j < runLen; j++)
                    parts.Add('_');
            }

            parts.Add(ConfusableChars[_random.Next(ConfusableChars.Length)]);
        }

        return new string(parts.ToArray());
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
