# Exact rational rounding

Load `Numerics/rational_round.gf` alongside `Numerics/Math.gf` for
`Rational.Round()` and `Round(MidpointRounding mode)`. Results are owning
BigIntegers. The default is nearest-integer rounding with midpoint ties to even.

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(-5l, 2l);
    BigInteger even = value.Round();
    BigInteger away = value.Round(MidpointRounding::AwayFromZero);
    if (even.ToLong() != -2l || away.ToLong() != -3l) return 1;
    return 0;
}
```

| Mode | Rule |
| --- | --- |
| `ToEven` | Nearest integer, ties to even |
| `AwayFromZero` | Nearest integer, ties away from zero |
| `ToZero` | Truncate toward zero |
| `ToNegativeInfinity` | Floor |
| `ToPositiveInfinity` | Ceiling |

All calculations use exact integer arithmetic, including for values too large
for native integers. Invalid modes throw before reading fraction components.
Comparisons and result construction can allocate. Failure leaves the source and
an assignment destination unchanged. A free `Round` function accepts a borrowed
`immutable(Rational)*` under `Standard::Numerics`.
