# Exact rational integral values

`Rational.Truncate()` returns a `BigInteger` rounded toward zero.
`Floor()` returns the greatest integer no greater than the fraction.
`Ceiling()` returns the least integer no smaller than the fraction.

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(-7l, 3l);
    BigInteger truncated = value.Truncate();
    BigInteger lower = value.Floor();
    BigInteger upper = value.Ceiling();
    if (truncated.ToLong() != -2l || lower.ToLong() != -3l || upper.ToLong() != -2l) return 1;
    return 0;
}
```

These methods use exact integer arithmetic without a floating-point conversion,
including when numerator, denominator, or result exceeds native integer ranges.
The returned integer owns its storage independently. Allocation failure throws
and leaves the source and any assignment destination unchanged.
