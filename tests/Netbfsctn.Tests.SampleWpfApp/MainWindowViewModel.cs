using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace Netbfsctn.Tests.SampleWpfApp;

public class MainWindowViewModel : INotifyPropertyChanged
{
	private string _inputText = "";
	private string _resultText = "";
	private const string SecretKey = "ObfuscationTestSecret_2024";

	public string InputText
	{
		get => _inputText;
		set { _inputText = value; OnPropertyChanged(nameof(InputText)); }
	}

	public string ResultText
	{
		get => _resultText;
		set { _resultText = value; OnPropertyChanged(nameof(ResultText)); }
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged(string name) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	public void Calculate()
	{
		if (int.TryParse(InputText, out var n))
		{
			var fib = Fibonacci(n);
			var factorial = Factorial(Math.Min(n, 20));
			ResultText = $"Fibonacci({n}) = {fib}\nFactorial({Math.Min(n, 20)}) = {factorial}";
		}
		else
		{
			ResultText = "Please enter a valid integer.";
		}
	}

	public void Transform()
	{
		var reversed = ReverseString(InputText);
		var hash = ComputeHash(InputText);
		var encrypted = XorEncrypt(InputText, SecretKey);
		ResultText = $"Reversed: {reversed}\nSHA256: {hash}\nXOR: {encrypted}";
	}

	/// <summary>
	/// Verify that all logic works correctly after obfuscation.
	/// Returns "OK" if all checks pass, otherwise an error message.
	/// </summary>
	public string RunVerification()
	{
		// Fibonacci check
		if (Fibonacci(10) != 55)
			return $"FAIL: Fibonacci(10) expected 55, got {Fibonacci(10)}";

		// Factorial check
		if (Factorial(5) != 120)
			return $"FAIL: Factorial(5) expected 120, got {Factorial(5)}";

		// String reverse check
		if (ReverseString("hello") != "olleh")
			return $"FAIL: ReverseString(\"hello\") expected \"olleh\", got {ReverseString("hello")}";

		// Hash determinism check
		var hash1 = ComputeHash("test");
		var hash2 = ComputeHash("test");
		if (hash1 != hash2)
			return "FAIL: Hash not deterministic";

		// XOR round-trip check
		var original = "Hello, WPF!";
		var encrypted = XorEncrypt(original, SecretKey);
		var decrypted = XorEncrypt(encrypted, SecretKey);
		if (decrypted != original)
			return $"FAIL: XOR round-trip failed, got \"{decrypted}\"";

		// INotifyPropertyChanged check
		var raised = false;
		PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(ResultText)) raised = true;
		};
		ResultText = "test";
		if (!raised)
			return "FAIL: PropertyChanged not raised";

		// Resource/style key string check (ensures string encryption doesn't break XAML keys)
		var buttonStyleKey = "ButtonStyle";
		if (string.IsNullOrEmpty(buttonStyleKey))
			return "FAIL: Style key is null or empty";

		return "OK";
	}

	private static long Fibonacci(int n)
	{
		if (n <= 1) return n;
		long a = 0, b = 1;
		for (var i = 2; i <= n; i++)
		{
			var temp = a + b;
			a = b;
			b = temp;
		}
		return b;
	}

	private static long Factorial(int n)
	{
		long result = 1;
		for (var i = 2; i <= n; i++)
			result *= i;
		return result;
	}

	private static string ReverseString(string input)
	{
		var chars = input.ToCharArray();
		Array.Reverse(chars);
		return new string(chars);
	}

	private static string ComputeHash(string input)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
		return Convert.ToHexString(bytes);
	}

	private static string XorEncrypt(string input, string key)
	{
		var sb = new StringBuilder(input.Length);
		for (var i = 0; i < input.Length; i++)
			sb.Append((char)(input[i] ^ key[i % key.Length]));
		return sb.ToString();
	}
}
