# Netbfsctn Benchmark Results

Date: 2026-03-13 23:39:41

## Summary

| Scenario | Size | Size Δ% | Time(ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|----------|---------|----------------|-------------------|---------------|---------|
| Baseline | 15.5 KB | - | 89.5 | - | 81/89 | 70 | 21.3 | OK |
| Rename Only | 15.5 KB | - | 89.3 | -0.2% | 30/89 | 70 | 21.3 | OK |
| Strings Only | 27.0 KB | +74.2% | 91.9 | +2.6% | 82/91 | 0 | 161.7 | OK |
| ControlFlow Only | 15.5 KB | - | 90.1 | +0.6% | 81/89 | 70 | 24.9 | OK |
| DeadCode Only | 16.0 KB | +3.2% | 87.6 | -2.2% | 81/99 | 70 | 27.1 | OK |
| Default (4 basic) | 29.0 KB | +87.1% | 86.0 | -3.9% | 31/102 | 0 | 146.0 | OK |
| + Anti-ILDASM | 29.0 KB | +87.1% | 87.8 | -2.0% | 33/104 | 0 | 143.8 | OK |
| + Anti-Debug | 29.5 KB | +90.3% | 85.7 | -4.3% | 32/103 | 0 | 145.5 | OK |
| + Anti-Tamper | 29.5 KB | +90.3% | 87.7 | -2.1% | 32/104 | 2 | 141.9 | OK |
| + NecroBit | 43.0 KB | +177.4% | 88.5 | -1.1% | 32/104 | 1 | 21.7 | OK |
| + HideCalls | 51.0 KB | +229.0% | 87.1 | -2.7% | 31/104 | 0 | 328.1 | OK |
| + Resources | 29.5 KB | +90.3% | 87.7 | -2.0% | 33/105 | 0 | 144.4 | OK |
| + Virtualize | 46.0 KB | +196.8% | 86.3 | -3.6% | 32/104 | 597 | 179.0 | OK |
| Full Protection | 85.5 KB | +451.6% | 87.1 | -2.7% | 38/115 | 10 | 303.8 | OK |

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
- Runtime: 89.3ms (-0.2%)
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
- Size: 27.0 KB (+74.2%)
- Runtime: 91.9ms (+2.6%)
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
- Runtime: 90.1ms (+0.6%)
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
- Runtime: 87.6ms (-2.2%)
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
- Runtime: 86.0ms (-3.9%)
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
- Runtime: 87.8ms (-2.0%)
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
- Runtime: 85.7ms (-4.3%)
- Correct: Yes

## + Anti-Tamper

- Types: 10 (readable: 1)
- Methods: 65 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 2
- Total IL instructions: 9225
- Avg IL/method: 141.9
- Has SuppressIldasm: False
- Resources: 2
- Size: 29.5 KB (+90.3%)
- Runtime: 87.7ms (-2.1%)
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
- Runtime: 88.5ms (-1.1%)
- Correct: Yes

## + HideCalls

- Types: 11 (readable: 1)
- Methods: 64 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 0
- Total IL instructions: 21001
- Avg IL/method: 328.1
- Has SuppressIldasm: False
- Resources: 1
- Size: 51.0 KB (+229.0%)
- Runtime: 87.1ms (-2.7%)
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
- Runtime: 87.7ms (-2.0%)
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
- Runtime: 86.3ms (-3.6%)
- Correct: Yes

## Full Protection

- Types: 15 (readable: 2)
- Methods: 70 (readable: 30)
- Fields: 30 (readable: 6)
- Plaintext strings: 10
- Total IL instructions: 21268
- Avg IL/method: 303.8
- Has SuppressIldasm: False
- Resources: 4
- Size: 85.5 KB (+451.6%)
- Runtime: 87.1ms (-2.7%)
- Correct: Yes
