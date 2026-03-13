namespace Netbfsctn.Benchmark.SampleApp.Services;

internal class Calculator
{
    private long _operationCount;

    internal long OperationCount => _operationCount;

    internal int Fibonacci(int n)
    {
        if (n <= 0) return 0;
        if (n == 1) return 1;

        int prev = 0, curr = 1;
        for (int i = 2; i <= n; i++)
        {
            int next = prev + curr;
            prev = curr;
            curr = next;
            _operationCount++;
        }
        return curr;
    }

    internal long Factorial(int n)
    {
        long result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
            _operationCount++;
        }
        return result;
    }

    internal bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2) return true;
        if (n % 2 == 0) return false;

        for (int i = 3; i * i <= n; i += 2)
        {
            _operationCount++;
            if (n % i == 0) return false;
        }
        return true;
    }

    internal int CountPrimes(int upTo)
    {
        int count = 0;
        for (int i = 2; i <= upTo; i++)
        {
            if (IsPrime(i)) count++;
        }
        return count;
    }

    internal double[] SortArray(double[] input)
    {
        var arr = (double[])input.Clone();
        for (int i = 0; i < arr.Length - 1; i++)
        {
            for (int j = 0; j < arr.Length - i - 1; j++)
            {
                _operationCount++;
                if (arr[j] > arr[j + 1])
                {
                    (arr[j], arr[j + 1]) = (arr[j + 1], arr[j]);
                }
            }
        }
        return arr;
    }

    internal string ClassifyNumber(int n)
    {
        return n switch
        {
            < 0 => "negative",
            0 => "zero",
            < 10 => "small",
            < 100 => "medium",
            < 1000 => "large",
            _ => "very large"
        };
    }
}
