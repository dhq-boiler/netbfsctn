namespace Netbfsctn.Benchmark.Analysis;

internal record AnalysisResult(
    int TotalTypes,
    int ReadableTypeNames,
    int TotalMethods,
    int ReadableMethodNames,
    int TotalFields,
    int ReadableFieldNames,
    int PlaintextStringCount,
    int TotalILInstructions,
    double AvgILInstructionsPerMethod,
    bool HasSuppressIldasmAttribute,
    int ResourceCount,
    long FileSizeBytes);
