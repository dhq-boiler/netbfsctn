using Netbfsctn.Core.NameGeneration;

namespace Netbfsctn.Tests;

public class ConfusableNameGeneratorTests
{
    [Fact]
    public void Next_GeneratesUniqueNames()
    {
        var generator = new ConfusableNameGenerator(seed: 42);
        var names = new HashSet<string>();

        for (var i = 0; i < 100; i++)
        {
            var name = generator.Next();
            Assert.True(names.Add(name), $"重複した名前が生成されました: {name}");
        }
    }

    [Fact]
    public void Next_GeneratesNamesWithConfusableCharsAndUnderscores()
    {
        var generator = new ConfusableNameGenerator(seed: 42);
        var allowedChars = new HashSet<char> { 'l', 'I', 'O', '0', 'o', '_' };

        for (var i = 0; i < 50; i++)
        {
            var name = generator.Next();
            foreach (var c in name)
            {
                Assert.Contains(c, allowedChars);
            }
        }
    }

    [Fact]
    public void Next_FirstCharIsNotDigit()
    {
        var generator = new ConfusableNameGenerator(seed: 42);

        for (var i = 0; i < 50; i++)
        {
            var name = generator.Next();
            Assert.False(char.IsDigit(name[0]), $"名前が数字で始まっています: {name}");
        }
    }

    [Fact]
    public void Next_MinimumLengthIsFour()
    {
        var generator = new ConfusableNameGenerator(seed: 42);

        for (var i = 0; i < 50; i++)
        {
            var name = generator.Next();
            Assert.True(name.Length >= 4, $"名前が短すぎます: {name} (長さ: {name.Length})");
        }
    }

    [Fact]
    public void Next_SomeNamesContainUnderscores()
    {
        var generator = new ConfusableNameGenerator(seed: 42);
        var hasUnderscore = false;

        for (var i = 0; i < 100; i++)
        {
            var name = generator.Next();
            if (name.Contains('_'))
            {
                hasUnderscore = true;
                break;
            }
        }

        Assert.True(hasUnderscore, "100個の名前の中にアンダースコアを含むものがありませんでした");
    }

    [Fact]
    public void Next_GeneratesVariedUnderscorePrefixes()
    {
        var generator = new ConfusableNameGenerator(seed: 42);
        var prefixLengths = new HashSet<int>();

        for (var i = 0; i < 200; i++)
        {
            var name = generator.Next();
            var prefixLen = 0;
            foreach (var c in name)
            {
                if (c == '_') prefixLen++;
                else break;
            }
            prefixLengths.Add(prefixLen);
        }

        // プレフィックス0個（なし）と1個以上の両方が出ること
        Assert.True(prefixLengths.Count >= 2,
            $"アンダースコアプレフィックスのバリエーションが不足: {string.Join(", ", prefixLengths)}");
    }
}
