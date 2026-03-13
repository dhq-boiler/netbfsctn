# Netbfsctn Benchmark Results

Date: 2026-03-13 23:23:33

## Summary

| Scenario | Size | Size Δ% | Time(ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|----------|---------|----------------|-------------------|---------------|---------|
| Baseline | 15.5 KB | - | 81.0 | - | 81/89 | 70 | 21.3 | OK |
| Rename Only | 15.5 KB | - | 85.2 | +5.2% | 30/89 | 70 | 21.3 | OK |
| Strings Only | 26.5 KB | +71.0% | 84.6 | +4.5% | 82/91 | 3 | 157.0 | OK |
| ControlFlow Only | 15.0 KB | -3.2% | 81.8 | +1.0% | 81/89 | 70 | 21.8 | OK |
| DeadCode Only | 16.5 KB | +6.5% | 85.4 | +5.4% | 81/99 | 70 | 27.1 | OK |
| Default (4 basic) | 28.5 KB | +83.9% | 85.9 | +6.0% | 31/102 | 3 | 139.4 | OK |
| + Anti-ILDASM | 28.0 KB | +80.6% | 86.6 | +6.9% | 33/104 | 3 | 137.3 | OK |
| + Anti-Debug | 28.5 KB | +83.9% | 86.7 | +7.0% | 32/103 | 3 | 139.0 | OK |
| + Anti-Tamper | 28.5 KB | +83.9% | 85.3 | +5.3% | 32/104 | 5 | 135.5 | OK |
| + NecroBit | 42.0 KB | +171.0% | 81.4 | +0.5% | 32/104 | 3 | 18.7 | OK |
| + HideCalls | 31.5 KB | +103.2% | 83.4 | +2.9% | 31/104 | 59 | 145.6 | OK |
| + Resources | 28.5 KB | +83.9% | 84.3 | +4.1% | 33/105 | 3 | 137.9 | OK |
| + Virtualize | 49.0 KB | +216.1% | 84.7 | +4.6% | 32/104 | 785 | 175.6 | OK |
| Full Protection | 52.5 KB | +238.7% | 85.1 | +5.0% | 38/115 | 136 | 39.0 | OK |

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
- Runtime: 85.2ms (+5.2%)
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
- Runtime: 84.6ms (+4.5%)
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
- Runtime: 81.8ms (+1.0%)
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
- Runtime: 85.4ms (+5.4%)
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
- Size: 28.5 KB (+83.9%)
- Runtime: 85.9ms (+6.0%)
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
- Size: 28.0 KB (+80.6%)
- Runtime: 86.6ms (+6.9%)
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
- Runtime: 86.7ms (+7.0%)
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
- Runtime: 85.3ms (+5.3%)
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
- Runtime: 81.4ms (+0.5%)
- Correct: Yes

## + HideCalls

- Types: 11 (readable: 1)
- Methods: 64 (readable: 25)
- Fields: 29 (readable: 5)
- Plaintext strings: 59
- Total IL instructions: 9316
- Avg IL/method: 145.6
- Has SuppressIldasm: False
- Resources: 1
- Size: 31.5 KB (+103.2%)
- Runtime: 83.4ms (+2.9%)
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
- Runtime: 84.3ms (+4.1%)
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
- Runtime: 84.7ms (+4.6%)
- Correct: Yes

## Full Protection

- Types: 15 (readable: 2)
- Methods: 70 (readable: 30)
- Fields: 30 (readable: 6)
- Plaintext strings: 136
- Total IL instructions: 2729
- Avg IL/method: 39.0
- Has SuppressIldasm: False
- Resources: 4
- Size: 52.5 KB (+238.7%)
- Runtime: 85.1ms (+5.0%)
- Correct: Yes
