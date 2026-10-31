# Calculate with exact fractions

`ratio-calc <left> <operator> <right>` performs exact arithmetic with Rational
values. Operators are `+`, `-`, `*`, and `/`.

```powershell
gflat build examples/ratio-calc/ratio-calc.gfproj -o artifacts/ratio-calc.exe
./artifacts/ratio-calc.exe 1/6 + 1/3
# 1/2
./artifacts/ratio-calc.exe -12/-18 '*' 3
# 2
./artifacts/ratio-calc.exe 2/3 / -4/5
# -5/6
```

An operand is an integer or a `numerator/denominator` pair. Integer parts accept
an optional sign and decimal digits. Negative denominators are allowed, and
zero denominators are rejected. Quote `'*'` when passing it through a shell.

Results are reduced, with positive denominators. Whole numbers omit `/1`, and
zero prints `0`.

Exit codes are 0 for success, 2 for invalid arguments, and 1 for division by a
zero Rational, allocation, or IO failure. Errors go to stderr.

See [Rational](../../docs/RATIONAL.md) for the arithmetic APIs.
