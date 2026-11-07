# Rational minimum, maximum, and clamping

`Rational::Min(first, second)` and `Max(first, second)` accept borrowed pointers
and return independent owning copies of the selected value.
`Clamp(value, minimum, maximum)` returns the value restricted to inclusive bounds.
Reversed bounds throw `Exception("Rational minimum exceeds maximum")`.

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(7l, 3l);
    Rational minimum = new(-1l, 2l);
    Rational maximum = new(3l, 2l);
    Rational result = Rational::Clamp(&value, &minimum, &maximum);
    if (!result.Equals(&maximum)) return 1;
    return 0;
}
```

Comparisons use exact integer arithmetic without floating-point conversion.
Bounds are validated before selection, including when both bounds are equal.
The source values remain unchanged. Comparisons and result copies may allocate.
On allocation failure, intermediate owners are released and an assignment
destination retains its previous value.
