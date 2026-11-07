# Rational magnitude and reciprocals

`Rational.Abs()` returns the nonnegative fraction with the same magnitude.
`Reciprocal()` returns its multiplicative inverse. Both return independent owning
values and leave the source unchanged.

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(-7l, 3l);
    Rational magnitude = value.Abs();
    Rational inverse = value.Reciprocal();
    Rational expected = new(7l, 3l);
    if (!magnitude.Equals(&expected)) return 1;
    expected = new(-3l, 7l);
    if (!inverse.Equals(&expected)) return 2;
    return 0;
}
```

Results stay reduced with positive denominators. There is no native-integer
magnitude limit, so absolute value also works for a numerator below `long`'s
minimum. Zero has absolute value zero but no reciprocal. Its `Reciprocal()` throws
`Exception("Rational reciprocal of zero")`.

Both operations can allocate. Failure leaves the source and an assignment
destination unchanged.
