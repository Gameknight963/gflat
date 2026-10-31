# Calculate with large integers

`big-calc <left> <operator> <right>` performs exact integer arithmetic.
Operators are `+`, `-`, `*`, `/`, `%`, `gcd`, and `lcm`.

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

Quote `'*'` so the shell passes it as an operator. Operands are decimal integers
with an optional sign. Leading zeroes are accepted.

Division truncates toward zero, and a nonzero remainder has the left operand's
sign. GCD and LCM ignore signs and return nonnegative results. `gcd(0, 0)` and
the LCM of any number with zero are zero.

Errors go to stderr. Exit codes are 0 for success, 2 for invalid arguments, and
1 for division by zero, allocation, or output failure.

See [BigInteger](../../docs/BIG_INTEGER.md) for the arithmetic APIs.
