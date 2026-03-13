# Netbfsctn Benchmark Results

Date: 2026-03-13 23:29:32

## Summary

| Scenario | Size | Size Δ% | Time(ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|----------|---------|----------------|-------------------|---------------|---------|
| Baseline | 15.5 KB | - | 84.8 | - | 81/89 | 70 | 21.3 | OK |
| Rename Only | 15.5 KB | - | 88.1 | +3.9% | 30/89 | 70 | 21.3 | OK |
| Strings Only | 26.5 KB | +71.0% | 88.8 | +4.7% | 82/91 | 3 | 157.0 | OK |
| ControlFlow Only | 15.0 KB | -3.2% | 88.6 | +4.5% | 81/89 | 70 | 21.8 | OK |
| DeadCode Only | 16.5 KB | +6.5% | 87.2 | +2.9% | 81/99 | 70 | 27.1 | OK |
| Default (4 basic) | 28.0 KB | +80.6% | 87.6 | +3.3% | 31/102 | 3 | 139.4 | OK |
| + Anti-ILDASM | 28.5 KB | +83.9% | 86.0 | +1.4% | 33/104 | 3 | 137.3 | OK |
| + Anti-Debug | 28.5 KB | +83.9% | 83.3 | -1.8% | 32/103 | 3 | 139.0 | OK |
| + Anti-Tamper | 28.5 KB | +83.9% | 85.3 | +0.6% | 32/104 | 5 | 135.5 | OK |
| + NecroBit | 42.0 KB | +171.0% | 84.0 | -0.9% | 32/104 | 3 | 18.7 | OK |
| + HideCalls | 51.5 KB | +232.3% | 86.0 | +1.4% | 31/104 | 3 | 329.6 | OK |
| + Resources | 28.5 KB | +83.9% | 84.1 | -0.8% | 33/105 | 3 | 137.9 | OK |
| + Virtualize | 49.0 KB | +216.1% | 87.5 | +3.1% | 32/104 | 785 | 175.6 | OK |
| Full Protection | 84.5 KB | +445.2% | 89.1 | +5.1% | 38/115 | 12 | 301.1 | OK |

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
- Runtime: 88.1ms (+3.9%)
- Correct: Yes

## Strings Only

- Types: 10 (readable: 8)
- Methods: 52 (readable: 49)
- Fields: 29 (readable: 25)
- Plaintext strings: 3
- Total IL instructions: 8164
- Avg IL/method: 157.0
- Has SuppressIldasm: False
- Resources: 1
- Size: 26.5 KB (+71.0%)
- Runtime: 88.8ms (+4.7%)
- Correct: Yes

## ControlFlow Only

- Types: 9 (readable: 8)
- Methods: 51 (readable: 48)
- Fields: 29 (readable: 25)
- Plaintext strings: 70
- Total IL instructions: 1110
- Avg IL/method: 21.8
- Has SuppressIldasm: False
- Resources: 1
- Size: 15.0 KB (-3.2%)
- Runtime: 88.6ms (+4.5%)
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
- Runtime: 87.2ms (+2.9%)
- Correct: Yes

## Default (4 basic)

- Types: 10 (readable: 1)
- Methods: 63 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 3
- Total IL instructions: 8784
- Avg IL/method: 139.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 28.0 KB (+80.6%)
- Runtime: 87.6ms (+3.3%)
- Correct: Yes

## + Anti-ILDASM

- Types: 11 (readable: 2)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 3
- Total IL instructions: 8787
- Avg IL/method: 137.3
- Has SuppressIldasm: False
- Resources: 1
- Size: 28.5 KB (+83.9%)
- Runtime: 86.0ms (+1.4%)
- Correct: Yes

## + Anti-Debug

- Types: 10 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 3
- Total IL instructions: 8895
- Avg IL/method: 139.0
- Has SuppressIldasm: False
- Resources: 1
- Size: 28.5 KB (+83.9%)
- Runtime: 83.3ms (-1.8%)
- Correct: Yes

## + Anti-Tamper

- Types: 10 (readable: 1)
- Methods: 65 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 5
- Total IL instructions: 8808
- Avg IL/method: 135.5
- Has SuppressIldasm: False
- Resources: 2
- Size: 28.5 KB (+83.9%)
- Runtime: 85.3ms (+0.6%)
- Correct: Yes

## + NecroBit

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 3
- Total IL instructions: 1199
- Avg IL/method: 18.7
- Has SuppressIldasm: False
- Resources: 2
- Size: 42.0 KB (+171.0%)
- Runtime: 84.0ms (-0.9%)
- Correct: Yes

## + HideCalls

- Types: 11 (readable: 1)
- Methods: 64 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 3
- Total IL instructions: 21096
- Avg IL/method: 329.6
- Has SuppressIldasm: False
- Resources: 1
- Size: 51.5 KB (+232.3%)
- Runtime: 86.0ms (+1.4%)
- Correct: Yes

## + Resources

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 30 (readable: 6)
- Plaintext strings: 3
- Total IL instructions: 8825
- Avg IL/method: 137.9
- Has SuppressIldasm: False
- Resources: 1
- Size: 28.5 KB (+83.9%)
- Runtime: 84.1ms (-0.8%)
- Correct: Yes

## + Virtualize

- Types: 11 (readable: 1)
- Methods: 64 (readable: 26)
- Fields: 29 (readable: 5)
- Plaintext strings: 785
- Total IL instructions: 11237
- Avg IL/method: 175.6
- Has SuppressIldasm: False
- Resources: 2
- Size: 49.0 KB (+216.1%)
- Runtime: 87.5ms (+3.1%)
- Correct: Yes

## Full Protection

- Types: 15 (readable: 2)
- Methods: 70 (readable: 30)
- Fields: 30 (readable: 6)
- Plaintext strings: 12
- Total IL instructions: 21077
- Avg IL/method: 301.1
- Has SuppressIldasm: False
- Resources: 4
- Size: 84.5 KB (+445.2%)
- Runtime: 89.1ms (+5.1%)
- Correct: Yes
