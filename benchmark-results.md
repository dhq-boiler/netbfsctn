# Netbfsctn Benchmark Results

Date: 2026-04-10 10:00:09

Powered by [BenchmarkDotNet](https://benchmarkdotnet.org/)

## Summary

| Scenario | Size | Size Δ% | Mean (ms) | StdDev (ms) | Median (ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|-----------|-------------|-------------|---------|----------------|-------------------|---------------|---------|
| Baseline | 18.5 KB | - | 83.4 | 1.8 | 83.6 | - | 132/143 | 91 | 18.6 | OK |
| Rename Only | 19.5 KB | +5.4% | 88.3 | 2.1 | 88.4 | +5.9% | 83/143 | 91 | 18.6 | OK |
| Strings Only | 33.5 KB | +81.1% | 86.9 | 2.1 | 87.2 | +4.2% | 133/145 | 0 | 137.6 | OK |
| ControlFlow Only | 18.5 KB | - | 82.4 | 1.1 | 82.5 | -1.2% | 132/143 | 91 | 19.4 | OK |
| DeadCode Only | 21.5 KB | +16.2% | 82.2 | 2.5 | 83.2 | -1.5% | 132/160 | 91 | 27.1 | OK |
| Default (4 basic) | 38.5 KB | +108.1% | 83.8 | 2.7 | 83.7 | +0.5% | 84/164 | 0 | 123.2 | OK |
| + Anti-ILDASM | 38.5 KB | +108.1% | 85.0 | 1.3 | 85.1 | +1.9% | 86/166 | 0 | 122.0 | OK |
| + Anti-Debug | 39.0 KB | +110.8% | 84.2 | 2.0 | 85.0 | +1.0% | 85/165 | 0 | 123.4 | OK |
| + Anti-Tamper | 38.5 KB | +108.1% | 85.1 | 1.6 | 85.3 | +2.1% | 85/166 | 1 | 121.0 | OK |
| + NecroBit | 53.0 KB | +186.5% | 87.6 | 3.3 | 87.6 | +5.1% | 85/166 | 1 | 42.8 | OK |
| + HideCalls | 62.5 KB | +237.8% | 85.8 | 1.6 | 85.9 | +2.9% | 84/166 | 0 | 252.2 | OK |
| + Resources | 39.0 KB | +110.8% | 83.7 | 1.5 | 83.9 | +0.3% | 86/167 | 0 | 122.4 | OK |
| + Virtualize | 46.0 KB | +148.6% | 84.1 | 1.6 | 83.5 | +0.9% | 85/166 | 17 | 109.3 | OK |
| Full Protection | 69.5 KB | +275.7% | 84.8 | 4.6 | 86.1 | +1.7% | 91/177 | 4 | 84.5 | OK |

## Baseline Assembly Details

- Types: 17 (readable: 16)
- Methods: 89 (readable: 85)
- Fields: 37 (readable: 31)
- Plaintext strings: 91
- Total IL instructions: 1450
- Resources: 1

## Rename Only

- Types: 17 (readable: 11)
- Methods: 89 (readable: 61)
- Fields: 37 (readable: 11)
- Plaintext strings: 91
- Total IL instructions: 1450
- Avg IL/method: 18.6
- Has SuppressIldasm: False
- Resources: 1
- Size: 19.5 KB (+5.4%)
- Runtime: 88.3 ± 2.1ms (median: 88.4ms, +5.9%)
- Correct: Yes

## Strings Only

- Types: 18 (readable: 16)
- Methods: 90 (readable: 86)
- Fields: 37 (readable: 31)
- Plaintext strings: 0
- Total IL instructions: 10870
- Avg IL/method: 137.6
- Has SuppressIldasm: False
- Resources: 1
- Size: 33.5 KB (+81.1%)
- Runtime: 86.9 ± 2.1ms (median: 87.2ms, +4.2%)
- Correct: Yes

## ControlFlow Only

- Types: 17 (readable: 16)
- Methods: 89 (readable: 85)
- Fields: 37 (readable: 31)
- Plaintext strings: 91
- Total IL instructions: 1514
- Avg IL/method: 19.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 18.5 KB (+0.0%)
- Runtime: 82.4 ± 1.1ms (median: 82.5ms, -1.2%)
- Correct: Yes

## DeadCode Only

- Types: 17 (readable: 16)
- Methods: 106 (readable: 85)
- Fields: 37 (readable: 31)
- Plaintext strings: 91
- Total IL instructions: 2579
- Avg IL/method: 27.1
- Has SuppressIldasm: False
- Resources: 1
- Size: 21.5 KB (+16.2%)
- Runtime: 82.2 ± 2.5ms (median: 83.2ms, -1.5%)
- Correct: Yes

## Default (4 basic)

- Types: 18 (readable: 11)
- Methods: 109 (readable: 62)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 12076
- Avg IL/method: 123.2
- Has SuppressIldasm: False
- Resources: 1
- Size: 38.5 KB (+108.1%)
- Runtime: 83.8 ± 2.7ms (median: 83.7ms, +0.5%)
- Correct: Yes

## + Anti-ILDASM

- Types: 19 (readable: 12)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 12079
- Avg IL/method: 122.0
- Has SuppressIldasm: False
- Resources: 1
- Size: 38.5 KB (+108.1%)
- Runtime: 85.0 ± 1.3ms (median: 85.1ms, +1.9%)
- Correct: Yes

## + Anti-Debug

- Types: 18 (readable: 11)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 12217
- Avg IL/method: 123.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 39.0 KB (+110.8%)
- Runtime: 84.2 ± 2.0ms (median: 85.0ms, +1.0%)
- Correct: Yes

## + Anti-Tamper

- Types: 18 (readable: 11)
- Methods: 111 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 1
- Total IL instructions: 12100
- Avg IL/method: 121.0
- Has SuppressIldasm: False
- Resources: 2
- Size: 38.5 KB (+108.1%)
- Runtime: 85.1 ± 1.6ms (median: 85.3ms, +2.1%)
- Correct: Yes

## + NecroBit

- Types: 19 (readable: 11)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 1
- Total IL instructions: 4241
- Avg IL/method: 42.8
- Has SuppressIldasm: False
- Resources: 2
- Size: 53.0 KB (+186.5%)
- Runtime: 87.6 ± 3.3ms (median: 87.6ms, +5.1%)
- Correct: Yes

## + HideCalls

- Types: 19 (readable: 11)
- Methods: 110 (readable: 62)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 24972
- Avg IL/method: 252.2
- Has SuppressIldasm: False
- Resources: 1
- Size: 62.5 KB (+237.8%)
- Runtime: 85.8 ± 1.6ms (median: 85.9ms, +2.9%)
- Correct: Yes

## + Resources

- Types: 19 (readable: 11)
- Methods: 110 (readable: 63)
- Fields: 38 (readable: 12)
- Plaintext strings: 0
- Total IL instructions: 12117
- Avg IL/method: 122.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 39.0 KB (+110.8%)
- Runtime: 83.7 ± 1.5ms (median: 83.9ms, +0.3%)
- Correct: Yes

## + Virtualize

- Types: 19 (readable: 11)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 17
- Total IL instructions: 10820
- Avg IL/method: 109.3
- Has SuppressIldasm: False
- Resources: 3
- Size: 46.0 KB (+148.6%)
- Runtime: 84.1 ± 1.6ms (median: 83.5ms, +0.9%)
- Correct: Yes

## Full Protection

- Types: 23 (readable: 12)
- Methods: 116 (readable: 67)
- Fields: 38 (readable: 12)
- Plaintext strings: 4
- Total IL instructions: 8872
- Avg IL/method: 84.5
- Has SuppressIldasm: False
- Resources: 5
- Size: 69.5 KB (+275.7%)
- Runtime: 84.8 ± 4.6ms (median: 86.1ms, +1.7%)
- Correct: Yes
