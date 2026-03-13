using System.Diagnostics;

namespace Netbfsctn.Benchmark.Runners;

internal record ProcessResult(int ExitCode, string StdOut, string StdErr, TimeSpan Elapsed);

internal static class ProcessRunner
{
    internal static ProcessResult Run(string fileName, string arguments, int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(true);
            throw new TimeoutException($"Process timed out after {timeoutMs}ms: {fileName} {arguments}");
        }

        sw.Stop();
        return new ProcessResult(process.ExitCode, stdout, stderr, sw.Elapsed);
    }
}
