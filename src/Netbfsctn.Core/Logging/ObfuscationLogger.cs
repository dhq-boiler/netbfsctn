namespace Netbfsctn.Core.Logging;

public class ObfuscationLogger
{
    private readonly bool _verbose;
    private readonly bool _quiet;

    public ObfuscationLogger(bool verbose = false, bool quiet = false)
    {
        _verbose = verbose;
        _quiet = quiet;
    }

    public void Info(string message)
    {
        if (!_quiet)
            Console.WriteLine(message);
    }

    public void Verbose(string message)
    {
        if (_verbose)
            Console.WriteLine($"  {message}");
    }

    public void Error(string message)
    {
        Console.Error.WriteLine($"ERROR: {message}");
    }

    public void Success(string message)
    {
        if (!_quiet)
            Console.WriteLine(message);
    }
}
