# Netbfsctn Benchmark Results

Date: 2026-04-10 02:18:22

## Summary

| Scenario | Size | Size Δ% | Time(ms) | Time Δ% | Readable Names | Plaintext Strings | Avg IL/Method | Correct |
|----------|------|---------|----------|---------|----------------|-------------------|---------------|---------|
| Baseline | 18.5 KB | - | 88.0 | - | 132/143 | 91 | 18.6 | OK |
| Rename Only | 20.0 KB | +8.1% | 86.0 | -2.3% | 81/143 | 91 | 18.6 | OK |
| Strings Only | 33.5 KB | +81.1% | 87.0 | -1.2% | 133/145 | 0 | 137.6 | OK |
| ControlFlow Only | 18.5 KB | - | 91.1 | +3.5% | 132/143 | 91 | 19.4 | OK |
| DeadCode Only | 21.5 KB | +16.2% | 90.9 | +3.3% | 132/160 | 91 | 27.1 | OK |
| Default (4 basic) | 38.5 KB | +108.1% | 90.2 | +2.4% | 82/164 | 0 | 123.2 | OK |
| + Anti-ILDASM | 38.5 KB | +108.1% | 88.1 | +0.1% | 84/166 | 0 | 122.0 | OK |
| + Anti-Debug | 39.0 KB | +110.8% | 86.0 | -2.3% | 83/165 | 0 | 123.4 | OK |
| + Anti-Tamper | 39.0 KB | +110.8% | 87.1 | -1.1% | 83/166 | 1 | 121.0 | OK |
| + NecroBit | 53.5 KB | +189.2% | 85.9 | -2.4% | 83/166 | 1 | 42.8 | OK |
| + HideCalls | 64.0 KB | +245.9% | 90.5 | +2.8% | 82/166 | 0 | 258.7 | OK |
| + Resources | 39.0 KB | +110.8% | 90.1 | +2.3% | 84/167 | 0 | 122.4 | OK |
| + Virtualize | 46.0 KB | +148.6% | 96.2 | +9.3% | 83/166 | 17 | 109.3 | OK |
| Full Protection | 69.5 KB | +275.7% | 91.9 | +4.4% | 89/177 | 4 | 84.5 | OK |

## Baseline Assembly Details

- Types: 17 (readable: 16)
- Methods: 89 (readable: 85)
- Fields: 37 (readable: 31)
- Plaintext strings: 91
- Total IL instructions: 1450
- Resources: 1

## Rename Only

- Types: 17 (readable: 9)
- Methods: 89 (readable: 61)
- Fields: 37 (readable: 11)
- Plaintext strings: 91
- Total IL instructions: 1450
- Avg IL/method: 18.6
- Has SuppressIldasm: False
- Resources: 1
- Size: 20.0 KB (+8.1%)
- Runtime: 86.0ms (-2.3%)
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
- Runtime: 87.0ms (-1.2%)
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
- Runtime: 91.1ms (+3.5%)
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
- Runtime: 90.9ms (+3.3%)
- Correct: Yes

## Default (4 basic)

- Types: 18 (readable: 9)
- Methods: 109 (readable: 62)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 12076
- Avg IL/method: 123.2
- Has SuppressIldasm: False
- Resources: 1
- Size: 38.5 KB (+108.1%)
- Runtime: 90.2ms (+2.4%)
- Correct: Yes

## + Anti-ILDASM

- Types: 19 (readable: 10)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 12079
- Avg IL/method: 122.0
- Has SuppressIldasm: False
- Resources: 1
- Size: 38.5 KB (+108.1%)
- Runtime: 88.1ms (+0.1%)
- Correct: Yes

## + Anti-Debug

- Types: 18 (readable: 9)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 12217
- Avg IL/method: 123.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 39.0 KB (+110.8%)
- Runtime: 86.0ms (-2.3%)
- Correct: Yes

## + Anti-Tamper

- Types: 18 (readable: 9)
- Methods: 111 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 1
- Total IL instructions: 12100
- Avg IL/method: 121.0
- Has SuppressIldasm: False
- Resources: 2
- Size: 39.0 KB (+110.8%)
- Runtime: 87.1ms (-1.1%)
- Correct: Yes

## + NecroBit

- Types: 19 (readable: 9)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 1
- Total IL instructions: 4241
- Avg IL/method: 42.8
- Has SuppressIldasm: False
- Resources: 2
- Size: 53.5 KB (+189.2%)
- Runtime: 85.9ms (-2.4%)
- Correct: Yes

## + HideCalls

- Types: 19 (readable: 9)
- Methods: 110 (readable: 62)
- Fields: 37 (readable: 11)
- Plaintext strings: 0
- Total IL instructions: 25616
- Avg IL/method: 258.7
- Has SuppressIldasm: False
- Resources: 1
- Size: 64.0 KB (+245.9%)
- Runtime: 90.5ms (+2.8%)
- Correct: Yes

## + Resources

- Types: 19 (readable: 9)
- Methods: 110 (readable: 63)
- Fields: 38 (readable: 12)
- Plaintext strings: 0
- Total IL instructions: 12117
- Avg IL/method: 122.4
- Has SuppressIldasm: False
- Resources: 1
- Size: 39.0 KB (+110.8%)
- Runtime: 90.1ms (+2.3%)
- Correct: Yes

## + Virtualize

- Types: 19 (readable: 9)
- Methods: 110 (readable: 63)
- Fields: 37 (readable: 11)
- Plaintext strings: 17
- Total IL instructions: 10820
- Avg IL/method: 109.3
- Has SuppressIldasm: False
- Resources: 3
- Size: 46.0 KB (+148.6%)
- Runtime: 96.2ms (+9.3%)
- Correct: Yes

## Full Protection

- Types: 23 (readable: 10)
- Methods: 116 (readable: 67)
- Fields: 38 (readable: 12)
- Plaintext strings: 4
- Total IL instructions: 8872
- Avg IL/method: 84.5
- Has SuppressIldasm: False
- Resources: 5
- Size: 69.5 KB (+275.7%)
- Runtime: 91.9ms (+4.4%)
- Correct: Yes
