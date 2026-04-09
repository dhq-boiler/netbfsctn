using System.Security.Cryptography;
using System.Text;
using Netbfsctn.Benchmark.SampleApp.Models;
using Netbfsctn.Benchmark.SampleApp.Services;

var verifyMode = args.Length > 0 && args[0] == "--verify";
var results = new StringBuilder();

// 1. Customer & Order
var customer = new Customer("Taro", "Yamada", 30, CustomerType.Premium);
var order1 = new Order(1001, new DateTime(2025, 1, 15));
order1.AddItem(new OrderItem("Widget", 3, 29.99m));
order1.AddItem(new OrderItem("Gadget", 1, 199.99m));
order1.Process();
order1.Process();
customer.AddOrder(order1);

var order2 = new Order(1002, new DateTime(2025, 2, 20));
order2.AddItem(new OrderItem("Doohickey", 5, 9.99m));
order2.Process();
customer.AddOrder(order2);

results.AppendLine(customer.GetDisplayInfo());
results.AppendLine(customer.GetLoyaltyTier());
results.AppendLine(order1.GetSummary());
results.AppendLine(order2.GetSummary());
results.AppendLine(customer.GetTotalSpent().ToString("F2"));

// 2. Calculator
var calc = new Calculator();
results.AppendLine(calc.Fibonacci(20).ToString());
results.AppendLine(calc.Factorial(12).ToString());
results.AppendLine(calc.IsPrime(97).ToString());
results.AppendLine(calc.CountPrimes(100).ToString());

var sorted = calc.SortArray([3.14, 1.41, 2.72, 0.58, 1.73]);
results.AppendLine(string.Join(",", sorted.Select(x => x.ToString("F2"))));

results.AppendLine(calc.ClassifyNumber(-5));
results.AppendLine(calc.ClassifyNumber(0));
results.AppendLine(calc.ClassifyNumber(7));
results.AppendLine(calc.ClassifyNumber(42));
results.AppendLine(calc.ClassifyNumber(999));
results.AppendLine(calc.ClassifyNumber(10000));

// 3. StringProcessor
var sp = new StringProcessor();
results.AppendLine(sp.GetGreeting("Benchmark"));
results.AppendLine(sp.FormatError("Main", "test error"));
results.AppendLine(sp.BuildReport(["Alpha", "Beta", "Gamma"]));
results.AppendLine(sp.Reverse("obfuscation"));
results.AppendLine(sp.CaesarCipher("Hello World", 13));
results.AppendLine(sp.GetAllConstants());

// 4. ResourceReader
var rr = new ResourceReader();
results.AppendLine(rr.ReadEmbeddedData());

// 5. Virtual dispatch (インターフェイス経由のメソッド/プロパティ/イベント)
results.AppendLine(VirtualDispatchDemo.Run());

if (verifyMode)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(results.ToString()));
    Console.WriteLine("CHECKSUM:" + Convert.ToHexString(hash));
}
else
{
    Console.Write(results.ToString());
}
