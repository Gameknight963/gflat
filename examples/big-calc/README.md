# Calculate with large integers

`big-calc <left> <operator> <right>` performs one exact integer operation. It
accepts `+`, `-`, `*`, `/`, `%`, or lowercase `gcd`. This is a direct command-line
example of the owning BigInteger arithmetic APIs, without an expression parser.

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
```

Quote `'*'` in both PowerShell and Bash so the shell passes a literal operator
instead of expanding filenames. Each operand is an optional leading `+` or `-`
followed by ASCII decimal digits. Leading zeroes are accepted; whitespace,
separators, radix prefixes, decimal points, and trailing text are rejected.
Shell argument-size and memory limits still apply.

Division truncates toward zero. A nonzero `%` remainder has the left operand's
sign; division or remainder by zero fails. `gcd` ignores signs and returns a
nonnegative result, with `gcd(0, 0)` equal to zero.

The tool streams canonical decimal output directly through `Format`, followed
by a platform newline and an explicit flush. It creates no intermediate owning
decimal string. BigInteger operands, intermediate results, and division scratch
storage use ordinary raw allocation and deterministic ownership cleanup. It
reads no files and performs no network or operating-system-specific operations.

Success exits `0`. Incorrect argument counts, unknown operators, and malformed
operands produce a diagnostic on stderr and exit `2`. Allocation failures,
division by zero, and output failures exit `1`. Parsing and computation complete
before output begins; an output failure can leave a printed prefix.

See [BigInteger](../../docs/BIG_INTEGER.md) for arithmetic and ownership contracts.
