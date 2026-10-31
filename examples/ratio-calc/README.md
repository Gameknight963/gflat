# Calculate with exact fractions

`ratio-calc <left> <operator> <right>` performs one exact Rational operation: `+`, `-`,
`*`, or `/`. Each operand is either an integer or a single `numerator/denominator` pair.
Rational parsing validates counted BigInteger parts and constructs canonical values.

```powershell
gflat build examples/ratio-calc/ratio-calc.gfproj -o artifacts/ratio-calc.exe
./artifacts/ratio-calc.exe 1/6 + 1/3
# 1/2
./artifacts/ratio-calc.exe -12/-18 '*' 3
# 2
./artifacts/ratio-calc.exe 2/3 / -4/5
# -5/6
```

## Operands and results

Quote `'*'` in PowerShell and Bash to pass a literal multiplication operator. Each
integer part accepts an optional leading `+` or `-` followed by ASCII decimal digits.
Leading zeros and negative denominators are allowed. Empty parts, multiple slashes, zero
denominators, whitespace, decimal points, radix prefixes and trailing text are rejected.
Shell argument and memory limits apply.

Results are reduced, denominators are positive, and zero prints `0`. Whole numbers omit
`/1`. Rational.Format streams directly to stdout, followed by a platform newline and an
explicit flush.

## Errors

Success exits `0`. Arity, operator and operand errors produce static stderr diagnostics
and exit `2`. A written denominator of zero is an operand error. Dividing by a valid
zero Rational, allocation failures and output failures exit `1` with a `ratio-calc: `
prefix followed by the underlying error message, such as `Rational divisor is zero` or
`Console flush failed`. Parsing and computation finish before output begins. Output
errors can leave a prefix in stdout.

See [Rational](../../docs/RATIONAL.md) for arithmetic and ownership contracts.
