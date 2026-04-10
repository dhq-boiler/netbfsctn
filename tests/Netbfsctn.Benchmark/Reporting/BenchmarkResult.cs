using Netbfsctn.Benchmark.Analysis;

namespace Netbfsctn.Benchmark.Reporting;

internal record BenchmarkResult(
    string ScenarioName,
    bool ObfuscationSuccess,
    string? ObfuscationError,
    AnalysisResult? Analysis,
    bool CorrectnessPass,
    double MeanRuntimeMs,
    double MedianRuntimeMs,
    double StdDevMs,
    double RuntimeDeltaPercent,
    double SizeDeltaPercent);
