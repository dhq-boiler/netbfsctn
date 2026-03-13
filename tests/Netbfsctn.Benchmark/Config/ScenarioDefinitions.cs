namespace Netbfsctn.Benchmark.Config;

internal static class ScenarioDefinitions
{
    internal static readonly BenchmarkScenario[] All =
    [
        new("Rename Only",       "--no-strings --no-control-flow --no-dead-code", "名前難読化のみ"),
        new("Strings Only",      "--no-rename --no-control-flow --no-dead-code",  "文字列暗号化のみ"),
        new("ControlFlow Only",  "--no-rename --no-strings --no-dead-code",       "制御フロー難読化のみ"),
        new("DeadCode Only",     "--no-rename --no-strings --no-control-flow",    "デッドコード挿入のみ"),
        new("Default (4 basic)", "",                                               "デフォルト4技法"),
        new("+ Anti-ILDASM",     "--anti-ildasm",                                 "デフォルト + Anti-ILDASM"),
        new("+ Anti-Debug",      "--anti-debug",                                  "デフォルト + Anti-Debug"),
        new("+ Anti-Tamper",     "--anti-tamper",                                 "デフォルト + Anti-Tampering"),
        new("+ NecroBit",        "--necrobit",                                    "デフォルト + NecroBit"),
        new("+ HideCalls",       "--hide-calls",                                  "デフォルト + HideCalls"),
        new("+ Resources",       "--protect-resources",                           "デフォルト + リソース保護"),
        new("+ Virtualize",      "--virtualize",                                  "デフォルト + コード仮想化"),
        new("Full Protection",   "--anti-ildasm --anti-debug --anti-tamper --necrobit --hide-calls --protect-resources --virtualize", "全保護"),
    ];
}
