# Exact rational powers

`Rational.Pow(int exponent)` returns an independent owning fraction raised to a
signed integer power. Negative powers invert the fraction. Zero to a negative
power throws before computing either part. Exponent zero returns one, including
for a zero base.

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(-2l, 3l);
    Rational powered = value.Pow(-3);
    Rational expected = new(-27l, 8l);
    if (!powered.Equals(&expected)) return 1;
    return 0;
}
```

Computation uses BigInteger powers without a floating-point conversion. Results
remain reduced with positive denominators. The full `int` exponent range is
accepted, including its minimum value. Large exponents can require very large
results and substantial time or memory.

The source stays unchanged. Allocation failure throws and releases intermediate
owners without replacing an assignment destination.
