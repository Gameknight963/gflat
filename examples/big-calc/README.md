# Calculate with large integers

`big-calc <left> <operator> <right>` performs one exact integer operation. It accepts
`+`, `-`, `*`, `/`, `%`, or lowercase `gcd` and `lcm`. It uses the owning BigInteger
arithmetic APIs.

```powershell
gflat build examples/big-calc/big-calc.gfproj -o artifacts/big-calc.exe
./artifacts/big-calc.exe 18446744073709551616 + 1
# 18446744073709551617
./artifacts/big-calc.exe 12345678901234567890 '*' 1000000000
# 12345678901234567890000000000
./artifacts/big-calc.exe -100000000000000000001 / 10000000001
# -9999999999
./artifacts/big-calc.exe -48 gcd 18
# 6
./artifacts/big-calc.exe -84 lcm 126
# 252
```

## Operands and results

Quote `'*'` in both PowerShell and Bash so the shell passes a literal operator instead
of expanding filenames. Each operand is an optional leading `+` or `-` followed by ASCII
decimal digits. Leading zeroes are accepted. Whitespace, separators, radix prefixes,
decimal points, and trailing text are rejected. Shell argument-size and memory limits
still apply.

Division truncates toward zero. A nonzero `%` remainder has the left operand's sign.
Division or remainder by zero fails. `gcd` ignores signs and returns a nonnegative
result, with `gcd(0, 0)` equal to zero. `lcm` also ignores signs and returns a
nonnegative result. Either zero operand gives zero. It divides out the GCD before
multiplying.

Results use canonical decimal notation and end with a platform newline.

## Errors

Success exits `0`. Incorrect argument counts, unknown operators, and malformed operands
produce a diagnostic on stderr and exit `2`. Allocation failures, division by zero, and
output failures exit `1`. Parsing and computation complete before output begins. An
output failure can leave a printed prefix.

See [BigInteger](../../docs/BIG_INTEGER.md) for arithmetic and ownership contracts.
