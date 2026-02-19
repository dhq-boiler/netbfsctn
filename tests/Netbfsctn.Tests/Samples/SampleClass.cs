using System;

namespace SampleApp;

internal class SampleClass
{
    private int _counter;
    private string _name;

    public SampleClass(string name)
    {
        _name = name;
        _counter = 0;
    }

    private void IncrementCounter()
    {
        _counter++;
    }

    private string GetGreeting()
    {
        return "Hello, " + _name + "!";
    }

    private int Calculate(int a, int b)
    {
        var sum = a + b;
        var product = a * b;
        return sum + product;
    }

    public void Run()
    {
        IncrementCounter();
        var greeting = GetGreeting();
        var result = Calculate(3, 4);
        Console.WriteLine(greeting);
        Console.WriteLine("Result: " + result);
    }
}
