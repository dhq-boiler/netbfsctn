# Netbfsctn Benchmark Results

Date: 2026-03-13 23:49:28

## Summary

| Scenario | Size | Size Δ% | Time(ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|----------|---------|----------------|-------------------|---------------|---------|
| Baseline | 15.5 KB | - | 91.9 | - | 81/89 | 70 | 21.3 | OK |
| Rename Only | 15.5 KB | - | 92.8 | +1.0% | 30/89 | 70 | 21.3 | OK |
| Strings Only | 26.5 KB | +71.0% | 94.1 | +2.4% | 82/91 | 0 | 161.7 | OK |
| ControlFlow Only | 15.5 KB | - | 92.9 | +1.1% | 81/89 | 70 | 24.9 | OK |
| DeadCode Only | 16.0 KB | +3.2% | 93.4 | +1.7% | 81/99 | 70 | 27.1 | OK |
| Default (4 basic) | 29.0 KB | +87.1% | 96.5 | +5.0% | 31/102 | 0 | 146.0 | OK |
| + Anti-ILDASM | 29.0 KB | +87.1% | 88.0 | -4.2% | 33/104 | 0 | 143.8 | OK |
| + Anti-Debug | 29.5 KB | +90.3% | 88.7 | -3.5% | 32/103 | 0 | 145.5 | OK |
| + Anti-Tamper | 29.5 KB | +90.3% | 94.3 | +2.6% | 32/104 | 1 | 141.9 | OK |
| + NecroBit | 43.0 KB | +177.4% | 92.8 | +1.0% | 32/104 | 1 | 21.7 | OK |
| + HideCalls | 52.5 KB | +238.7% | 93.9 | +2.2% | 31/104 | 0 | 339.7 | OK |
| + Resources | 29.5 KB | +90.3% | 94.6 | +2.9% | 33/105 | 0 | 144.4 | OK |
| + Virtualize | 38.0 KB | +145.2% | 94.4 | +2.7% | 32/104 | 22 | 142.3 | OK |
| Full Protection | 56.0 KB | +261.3% | 95.2 | +3.6% | 38/115 | 3 | 69.7 | OK |

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
- Runtime: 92.8ms (+1.0%)
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
- Runtime: 94.1ms (+2.4%)
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
- Runtime: 92.9ms (+1.1%)
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
- Size: 16.0 KB (+3.2%)
- Runtime: 93.4ms (+1.7%)
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
- Runtime: 96.5ms (+5.0%)
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
- Runtime: 88.0ms (-4.2%)
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
- Runtime: 88.7ms (-3.5%)
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
- Runtime: 94.3ms (+2.6%)
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
- Runtime: 92.8ms (+1.0%)
- Correct: Yes

## + HideCalls

- Types: 11 (readable: 1)
- Methods: 64 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 0
- Total IL instructions: 21741
- Avg IL/method: 339.7
- Has SuppressIldasm: False
- Resources: 1
- Size: 52.5 KB (+238.7%)
- Runtime: 93.9ms (+2.2%)
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
- Runtime: 94.6ms (+2.9%)
- Correct: Yes

## + Virtualize

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 22
- Total IL instructions: 9110
- Avg IL/method: 142.3
- Has SuppressIldasm: False
- Resources: 3
- Size: 38.0 KB (+145.2%)
- Runtime: 94.4ms (+2.7%)
- Correct: Yes

## Full Protection

- Types: 15 (readable: 2)
- Methods: 70 (readable: 30)
- Fields: 30 (readable: 6)
- Plaintext strings: 3
- Total IL instructions: 4876
- Avg IL/method: 69.7
- Has SuppressIldasm: False
- Resources: 5
- Size: 56.0 KB (+261.3%)
- Runtime: 95.2ms (+3.6%)
- Correct: Yes
