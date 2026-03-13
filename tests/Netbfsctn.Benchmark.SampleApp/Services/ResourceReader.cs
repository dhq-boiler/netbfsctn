using System.Reflection;

namespace Netbfsctn.Benchmark.SampleApp.Services;

internal class ResourceReader
{
    internal string ReadEmbeddedData()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Data.txt"));

        if (resourceName == null)
            return "Resource not found";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return "Stream is null";

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
