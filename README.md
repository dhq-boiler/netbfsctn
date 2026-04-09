# netbfsctn

A .NET assembly (DLL / EXE) obfuscation CLI tool.
Provides 11 obfuscation techniques through IL-level manipulation powered by [dnlib](https://github.com/0xd4d/dnlib).

## Features

- **11 obfuscation techniques** — from name obfuscation to code virtualization
- **Multi-assembly processing** — shared NameMap maintains cross-assembly reference integrity
- **C++/CLI (mixed-mode) support** — automatic `NativeModuleWriter` switching
- **Source code obfuscation mode** — Roslyn-based AST rewriters also available
- **CLI-based** — easily integrates into CI/CD pipelines
- **.NET 10.0** compatible

## Installation

```bash
git clone https://github.com/dhq-boiler/netbfsctn.git
cd netbfsctn
dotnet build -c Release
```

## Usage

### Basic (default 4 techniques applied)

```bash
netbfsctn MyApp.dll -o MyApp.obf.dll
```

The following 4 techniques are enabled by default:

- Name obfuscation
- String encryption (XOR)
- Control flow obfuscation
- Dead code insertion

### Full protection

```bash
netbfsctn MyApp.dll -o MyApp.obf.dll \
  --anti-ildasm --anti-debug --anti-tamper \
  --necrobit --hide-calls --virtualize \
  --protect-resources --mapping-file map.json
```

### Public member renaming (cross-assembly safe)

```bash
netbfsctn App.dll -o App.obf.dll \
  --additional-input NativeLib.dll \
  --rename-public
```

Renames public types, methods, fields, properties, and events — including virtual method groups.
Cross-assembly references are automatically synchronized. WPF assemblies (PresentationFramework references) are auto-excluded.

```bash
# Exclude specific assemblies from public renaming
netbfsctn App.dll --rename-public --exclude-rename-public PluginApi
```

### Multi-assembly obfuscation

```bash
netbfsctn App.dll -o App.obf.dll \
  --additional-input Lib.dll \
  --additional-output Lib.obf.dll
```

Shares the same `ObfuscationContext` (NameMap / NameGenerator), so cross-assembly type references remain intact.

### Source code obfuscation mode

```bash
netbfsctn ./src -m source -o ./obfuscated
```

## Obfuscation Techniques

| # | Technique | Option | Default | Description |
|---|-----------|--------|---------|-------------|
| 1 | Name Obfuscation | `--no-rename` to disable | Enabled | Renames types, methods, fields, properties, and parameters to confusable characters (l, I, 0, O, o). Virtual methods/properties/events are renamed as groups to maintain dispatch integrity. |
| 2 | String Encryption | `--no-strings` / `--encryption aes` | Enabled (XOR) | Converts plaintext strings to byte arrays + decryption helper calls |
| 3 | Control Flow | `--no-control-flow` to disable | Enabled | Transforms methods into state machines with switch dispatchers |
| 4 | Dead Code Insertion | `--no-dead-code` to disable | Enabled | Inserts unreachable methods and code blocks |
| 5 | Anti-ILDASM | `--anti-ildasm` | Disabled | Injects SuppressIldasmAttribute to prevent ILDASM disassembly |
| 6 | Anti-Debug | `--anti-debug` | Disabled | Injects `Debugger.IsAttached` checks into module initializer + ~30% of methods |
| 7 | Anti-Tampering | `--anti-tamper` | Disabled | SHA256 hash-based assembly tampering detection |
| 8 | HideMethodCalls | `--hide-calls` | Disabled | Replaces method calls with reflection (`Type.GetMethod` + `Invoke`) |
| 9 | NecroBit | `--necrobit` | Disabled | XOR-encrypts method bodies into embedded resources, dynamically restored at runtime |
| 10 | Code Virtualization | `--virtualize` | Disabled | Converts IL to custom VM bytecode, executed by an injected VM interpreter |
| 11 | Resource Protection | `--protect-resources` | Disabled | Encrypts embedded resources and obfuscates resource names |

### Granular rename control

Combine `--no-rename` with individual options to fine-tune renaming targets:

```bash
# Rename only types and methods (leave fields and properties unchanged)
netbfsctn MyApp.dll --no-rename --rename-types --rename-methods
```

| Option | Description |
|--------|-------------|
| `--rename-types` | Rename types only |
| `--rename-fields` | Rename fields only |
| `--rename-methods` | Rename methods only |
| `--rename-properties` | Rename properties only |
| `--rename-public` | Rename public members (with cross-assembly reference fixing) |
| `--exclude-rename-public` | Exclude specific assemblies from public renaming (multiple allowed) |

## Benchmarks

Results for a sample app (15.5 KB, 51 methods, 89 types, 70 plaintext strings):

| Configuration | Size | Size Increase | Runtime Overhead | Plaintext Strings | Avg IL/Method | Correct |
|--------------|------|--------------|-----------------|-------------------|---------------|---------|
| Baseline | 15.5 KB | — | — | 70 | 21.3 | OK |
| Rename Only | 15.5 KB | ±0% | +1.0% | 70 | 21.3 | OK |
| Strings Only | 26.5 KB | +71.0% | +2.4% | 0 | 161.7 | OK |
| ControlFlow Only | 15.5 KB | ±0% | +1.1% | 70 | 24.9 | OK |
| DeadCode Only | 16.0 KB | +3.2% | +1.7% | 70 | 27.1 | OK |
| **Default (4 basic)** | **29.0 KB** | **+87.1%** | **+5.0%** | **0** | **146.0** | **OK** |
| + Anti-ILDASM | 29.0 KB | +87.1% | -4.2% | 0 | 143.8 | OK |
| + Anti-Debug | 29.5 KB | +90.3% | -3.5% | 0 | 145.5 | OK |
| + Anti-Tamper | 29.5 KB | +90.3% | +2.6% | 1 | 141.9 | OK |
| + NecroBit | 43.0 KB | +177.4% | +1.0% | 1 | 21.7 | OK |
| + HideCalls | 52.5 KB | +238.7% | +2.2% | 0 | 339.7 | OK |
| + Resources | 29.5 KB | +90.3% | +2.9% | 0 | 144.4 | OK |
| + Virtualize | 38.0 KB | +145.2% | +2.7% | 22 | 142.3 | OK |
| **Full Protection** | **56.0 KB** | **+261.3%** | **+3.6%** | **3** | **69.7** | **OK** |

> All configurations pass correctness verification. Full protection adds only +261% size and +3.6% runtime overhead.

## Project Structure

```
src/
├── Netbfsctn.Cli/       # CLI entry point (System.CommandLine)
├── Netbfsctn.Core/      # Pipeline abstractions, encryption, name generation
├── Netbfsctn.IL/        # IL-based obfuscation (dnlib), VM subsystem
└── Netbfsctn.Source/    # Source code obfuscation (Roslyn AST rewriters)

tests/
├── Netbfsctn.Tests/                  # Unit & integration tests
├── Netbfsctn.Tests.SampleCppCli/     # C++/CLI test library (vcxproj)
├── Netbfsctn.Tests.CppCliHarness/    # C# harness for C++/CLI tests
├── Netbfsctn.Tests.SampleWpfApp/     # WPF sample for obfuscation tests
├── Netbfsctn.Benchmark/              # Benchmark suite
└── Netbfsctn.Benchmark.SampleApp/    # Sample app for benchmarking
```

## CLI Options

```
netbfsctn <input> [options]

Arguments:
  input                     Input path (DLL/EXE or .cs file/directory)

Options:
  -o, --output              Output path
  -m, --mode                Obfuscation mode (il|source)
  --encryption              Encryption method (xor|aes) [default: xor]
  -v, --verbose             Verbose output
  -q, --quiet               Minimal output

Basic techniques (enabled by default):
  --no-rename               Disable name obfuscation
  --rename-types            Rename types only
  --rename-fields           Rename fields only
  --rename-methods          Rename methods only
  --rename-properties       Rename properties only
  --rename-public           Rename public members (cross-assembly safe)
  --exclude-rename-public   Exclude assemblies from public renaming
  --no-strings              Disable string encryption
  --no-control-flow         Disable control flow obfuscation
  --no-dead-code            Disable dead code insertion

Advanced techniques (opt-in):
  --anti-ildasm             Add Anti-ILDASM attribute
  --anti-debug              Inject debugger detection code
  --anti-tamper             Inject tampering detection code
  --necrobit                Encrypt method bodies
  --hide-calls              Replace method calls with reflection
  --virtualize              Convert methods to custom VM bytecode
  --protect-resources       Encrypt embedded resources

Multi-assembly:
  --additional-input        Additional input assembly paths (multiple allowed)
  --additional-output       Additional output assembly paths (multiple allowed)

Other:
  --mapping-file            Output name mapping file (path optional)
```

## License

MIT
