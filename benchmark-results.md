# Netbfsctn Benchmark Results

Date: 2026-03-13 23:43:57

## Summary

| Scenario | Size | Size Δ% | Time(ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|----------|---------|----------------|-------------------|---------------|---------|
| Baseline | 15.5 KB | - | 93.5 | - | 81/89 | 70 | 21.3 | OK |
| Rename Only | 15.5 KB | - | 93.3 | -0.1% | 30/89 | 70 | 21.3 | OK |
| Strings Only | 26.5 KB | +71.0% | 95.6 | +2.3% | 82/91 | 0 | 161.7 | OK |
| ControlFlow Only | 15.5 KB | - | 96.6 | +3.4% | 81/89 | 70 | 24.9 | OK |
| DeadCode Only | 16.5 KB | +6.5% | 92.9 | -0.6% | 81/99 | 70 | 27.1 | OK |
| Default (4 basic) | 29.0 KB | +87.1% | 92.0 | -1.5% | 31/102 | 0 | 146.0 | OK |
| + Anti-ILDASM | 29.0 KB | +87.1% | 90.5 | -3.2% | 33/104 | 0 | 143.8 | OK |
| + Anti-Debug | 29.5 KB | +90.3% | 92.2 | -1.3% | 32/103 | 0 | 145.5 | OK |
| + Anti-Tamper | 29.5 KB | +90.3% | 92.0 | -1.6% | 32/104 | 1 | 141.9 | OK |
| + NecroBit | 43.0 KB | +177.4% | 96.4 | +3.1% | 32/104 | 1 | 21.7 | OK |
| + HideCalls | 51.5 KB | +232.3% | 96.1 | +2.9% | 31/104 | 0 | 330.6 | OK |
| + Resources | 29.5 KB | +90.3% | 96.3 | +3.0% | 33/105 | 0 | 144.4 | OK |
| + Virtualize | 46.0 KB | +196.8% | 89.6 | -4.1% | 32/104 | 597 | 179.0 | OK |
| Full Protection | 85.5 KB | +451.6% | 90.2 | -3.4% | 38/115 | 9 | 303.8 | OK |

## Baseline Assembly Details

- Types: 9 (readable: 8)
- Methods: 51 (readable: 48)
- Fields: 29 (readable: 25)
- Plaintext strings: 70
- Total IL instructions: 1084
- Resources: 1

## Rename Only

- Types: 9 (readable: 1)
- Methods: 51 (readable: 24)
- Fields: 29 (readable: 5)
- Plaintext strings: 70
- Total IL instructions: 1084
- Avg IL/method: 21.3
- Has SuppressIldasm: False
- Resources: 1
- Size: 15.5 KB (+0.0%)
- Runtime: 93.3ms (-0.1%)
- Correct: Yes

## Strings Only

- Types: 10 (readable: 8)
- Methods: 52 (readable: 49)
- Fields: 29 (readable: 25)
- Plaintext strings: 0
- Total IL instructions: 8408
- Avg IL/method: 161.7
- Has SuppressIldasm: False
- Resources: 1
- Size: 26.5 KB (+71.0%)
- Runtime: 95.6ms (+2.3%)
- Correct: Yes

## ControlFlow Only

- Types: 9 (readable: 8)
- Methods: 51 (readable: 48)
- Fields: 29 (readable: 25)
- Plaintext strings: 70
- Total IL instructions: 1268
- Avg IL/method: 24.9
- Has SuppressIldasm: False
- Resources: 1
- Size: 15.5 KB (+0.0%)
- Runtime: 96.6ms (+3.4%)
- Correct: Yes

## DeadCode Only

- Types: 9 (readable: 8)
- Methods: 61 (readable: 48)
- Fields: 29 (readable: 25)
- Plaintext strings: 70
- Total IL instructions: 1654
- Avg IL/method: 27.1
- Has SuppressIldasm: False
- Resources: 1
- Size: 16.5 KB (+6.5%)
- Runtime: 92.9ms (-0.6%)
- Correct: Yes

## Default (4 basic)

- Types: 10 (readable: 1)
- Methods: 63 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 0
- Total IL instructions: 9201
- Avg IL/method: 146.0
- Has SuppressIldasm: False
- Resources: 1
- Size: 29.0 KB (+87.1%)
- Runtime: 92.0ms (-1.5%)
- Correct: Yes

## + Anti-ILDASM

- Types: 11 (readable: 2)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 0
- Total IL instructions: 9204
- Avg IL/method: 143.8
- Has SuppressIldasm: False
- Resources: 1
- Size: 29.0 KB (+87.1%)
- Runtime: 90.5ms (-3.2%)
- Correct: Yes

## + Anti-Debug

- Types: 10 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 0
- Total IL instructions: 9312
- Avg IL/method: 145.5
- Has SuppressIldasm: False
- Resources: 1
- Size: 29.5 KB (+90.3%)
- Runtime: 92.2ms (-1.3%)
- Correct: Yes

## + Anti-Tamper

- Types: 10 (readable: 1)
- Methods: 65 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 1
- Total IL instructions: 9225
- Avg IL/method: 141.9
- Has SuppressIldasm: False
- Resources: 2
- Size: 29.5 KB (+90.3%)
- Runtime: 92.0ms (-1.6%)
- Correct: Yes

## + NecroBit

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 1
- Total IL instructions: 1390
- Avg IL/method: 21.7
- Has SuppressIldasm: False
- Resources: 2
- Size: 43.0 KB (+177.4%)
- Runtime: 96.4ms (+3.1%)
- Correct: Yes

## + HideCalls

- Types: 11 (readable: 1)
- Methods: 64 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 0
- Total IL instructions: 21157
- Avg IL/method: 330.6
- Has SuppressIldasm: False
- Resources: 1
- Size: 51.5 KB (+232.3%)
- Runtime: 96.1ms (+2.9%)
- Correct: Yes

## + Resources

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 30 (readable: 6)
- Plaintext strings: 0
- Total IL instructions: 9242
- Avg IL/method: 144.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 29.5 KB (+90.3%)
- Runtime: 96.3ms (+3.0%)
- Correct: Yes

## + Virtualize

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 597
- Total IL instructions: 11454
- Avg IL/method: 179.0
- Has SuppressIldasm: False
- Resources: 2
- Size: 46.0 KB (+196.8%)
- Runtime: 89.6ms (-4.1%)
- Correct: Yes

## Full Protection

- Types: 15 (readable: 2)
- Methods: 70 (readable: 30)
- Fields: 30 (readable: 6)
- Plaintext strings: 9
- Total IL instructions: 21268
- Avg IL/method: 303.8
- Has SuppressIldasm: False
- Resources: 4
- Size: 85.5 KB (+451.6%)
- Runtime: 90.2ms (-3.4%)
- Correct: Yes
