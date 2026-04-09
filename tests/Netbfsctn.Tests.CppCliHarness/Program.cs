using SampleCppCli;

if (args.Length > 0 && args[0] == "--verify")
{
    var result = Verifier.RunAllTests();
    Console.Write(result);
}
else
{
    Console.WriteLine("Usage: --verify");
    Console.WriteLine("Runs all C++/CLI verification tests and outputs CHECKSUM.");
}
