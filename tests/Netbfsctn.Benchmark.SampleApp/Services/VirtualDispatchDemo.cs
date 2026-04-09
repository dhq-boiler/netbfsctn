namespace Netbfsctn.Benchmark.SampleApp.Services;

/// <summary>
/// virtual メソッド / プロパティ / イベント のディスパッチを検証するデモ。
/// インターフェイス経由の呼び出しが難読化後も正しく動作することを確認する。
/// </summary>
public interface IGreeter
{
    string Greet(string name);
    string Farewell(string name);
    string Name { get; }
    int GreetCount { get; }
    event EventHandler<string>? Greeted;
}

public class FormalGreeter : IGreeter
{
    private int _greetCount;

    public string Name => "Formal";
    public int GreetCount => _greetCount;
    public event EventHandler<string>? Greeted;

    public string Greet(string name)
    {
        _greetCount++;
        Greeted?.Invoke(this, name);
        return $"Good day, {name}. How do you do?";
    }

    public string Farewell(string name) => $"Farewell, {name}. Until we meet again.";
}

public class CasualGreeter : IGreeter
{
    private int _greetCount;

    public string Name => "Casual";
    public int GreetCount => _greetCount;
    public event EventHandler<string>? Greeted;

    public string Greet(string name)
    {
        _greetCount++;
        Greeted?.Invoke(this, name);
        return $"Hey {name}! What's up?";
    }

    public string Farewell(string name) => $"Later, {name}!";
}

public interface IProcessor
{
    bool IsEnabled { get; set; }
    string Process(string input);
    void Reset();
}

public abstract class ProcessorBase : IProcessor
{
    public bool IsEnabled { get; set; } = true;
    public abstract string Process(string input);

    public virtual void Reset()
    {
        IsEnabled = true;
    }
}

public class UpperProcessor : ProcessorBase
{
    public override string Process(string input) => IsEnabled ? input.ToUpperInvariant() : input;
}

public class ReverseProcessor : ProcessorBase
{
    public override string Process(string input)
    {
        if (!IsEnabled) return input;
        var chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    public override void Reset()
    {
        base.Reset();
        // カスタムリセットロジック
    }
}

public static class VirtualDispatchDemo
{
    public static string Run()
    {
        var results = new List<string>();

        // インターフェイス経由の virtual メソッド呼び出し
        IGreeter[] greeters = [new FormalGreeter(), new CasualGreeter()];
        var greetedNames = new List<string>();

        foreach (var greeter in greeters)
        {
            greeter.Greeted += (_, name) => greetedNames.Add($"{greeter.Name}:{name}");
            results.Add(greeter.Greet("Alice"));
            results.Add(greeter.Greet("Bob"));
            results.Add(greeter.Farewell("Charlie"));
            results.Add($"{greeter.Name}.Count={greeter.GreetCount}");
        }

        results.Add($"Events={string.Join(",", greetedNames)}");

        // 抽象基底クラス経由の virtual ディスパッチ
        IProcessor[] processors = [new UpperProcessor(), new ReverseProcessor()];
        foreach (var proc in processors)
        {
            results.Add(proc.Process("Hello World"));
            proc.IsEnabled = false;
            results.Add(proc.Process("Hello World"));
            proc.Reset();
            results.Add($"AfterReset.Enabled={proc.IsEnabled}");
        }

        return string.Join("\n", results);
    }
}
