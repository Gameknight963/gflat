# BigInteger powers with signed exponents

`BigInteger.Pow(int exponent)` accepts a nonnegative signed exponent and returns
an independent owning integer. Negative exponents throw
`Exception("BigInteger exponent must not be negative")` without changing the
source or an assignment destination.

```csharp
using Standard::Numerics;

int main()
{
    BigInteger value = new(-3l);
    int exponent = 5;
    BigInteger powered = value.Pow(exponent);
    if (powered.ToLong() != -243l) return 1;
    return 0;
}
```

The existing `Pow(uint)` overload remains available for the full unsigned range.
Both overloads use the same exact integer algorithm. Exponent zero returns one,
including for zero raised to zero. Positive powers of zero return zero without
allocating result storage. Other results can allocate and throw. Large powers
can require substantial time and memory.

Use `Rational.Pow(int)` when a negative exponent should return an exact fraction.
