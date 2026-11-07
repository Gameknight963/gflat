# Inspecting rational values

`Rational` exposes allocation-free, `nothrow` value properties:

| Property | Meaning |
| --- | --- |
| `Sign` | -1, 0, or 1 for negative, zero, or positive values |
| `IsZero` | The value is exactly zero |
| `IsOne` | The value is exactly one |
| `IsInteger` | The value has no fractional part |

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(-6l, -3l);
    if (value.Sign != 1 || !value.IsInteger || value.IsOne || value.IsZero) return 1;
    Rational zero = new();
    if (!zero.IsZero || !zero.IsInteger || zero.Sign != 0) return 2;
    return 0;
}
```

These checks use the canonical fraction directly and do not copy the owning
`Numerator` or `Denominator` values. Zero and negative whole numbers count as
integers. Equivalent input fractions are normalized before inspection, so `2/2`
has `IsOne == true`, and `6/3` has `IsInteger == true`.
