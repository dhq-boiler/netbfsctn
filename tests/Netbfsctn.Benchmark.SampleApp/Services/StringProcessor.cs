namespace Netbfsctn.Benchmark.SampleApp.Services;

internal class StringProcessor
{
    private const string Greeting = "Hello, World!";
    private const string Farewell = "Goodbye, cruel world...";
    private const string SecretMessage = "The encryption key is hidden in plain sight";
    private const string ErrorTemplate = "Error occurred at {0}: {1}";
    private const string SuccessMessage = "Operation completed successfully";
    private const string WarningPrefix = "[WARNING] ";
    private const string ConnectionString = "Server=localhost;Database=TestDb;User=admin;Password=secret123";

    internal string GetGreeting(string name)
    {
        return Greeting.Replace("World", name);
    }

    internal string FormatError(string location, string message)
    {
        return string.Format(ErrorTemplate, location, message);
    }

    internal string BuildReport(string[] items)
    {
        var header = "=== Report Start ===";
        var footer = "=== Report End ===";
        var body = "";

        for (int i = 0; i < items.Length; i++)
        {
            body += $"  [{i + 1}] {items[i]}\n";
        }

        return header + "\n" + body + footer;
    }

    internal string Reverse(string input)
    {
        char[] chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    internal string CaesarCipher(string input, int shift)
    {
        var result = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsLetter(c))
            {
                char baseChar = char.IsUpper(c) ? 'A' : 'a';
                result[i] = (char)(((c - baseChar + shift) % 26 + 26) % 26 + baseChar);
            }
            else
            {
                result[i] = c;
            }
        }
        return new string(result);
    }

    internal string GetAllConstants()
    {
        return string.Join("|", Greeting, Farewell, SecretMessage, SuccessMessage, WarningPrefix, ConnectionString);
    }
}
