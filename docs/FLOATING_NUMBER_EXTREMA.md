# Select numeric floating-point extrema

`Math::MinNumber`, `MaxNumber`, `MinMagnitudeNumber`, and
`MaxMagnitudeNumber` accept `float` or `double` values. When exactly one input
is NaN, they return the numeric input. When both are NaN, they return NaN.
No particular NaN payload is promised.

`MinNumber` and `MaxNumber` compare numeric values. The magnitude variants
compare absolute values and return the original signed operand. Equal
magnitudes select the negative operand for minimum and the positive operand
for maximum. This also applies to negative and positive zero.

```csharp
using Standard::Numerics;

int main()
{
    uint nanBits = 0x7fc00001u;
    float nan = nanBits.reinterpret<float>();
    if (Math::MinNumber(nan, 4.0f) != 4.0f) return 1;
    if (Math::MaxNumber(-3.0f, nan) != -3.0f) return 2;
    if (Math::MinMagnitudeNumber(-4.0, 4.0) != -4.0) return 3;
    if (Math::MaxMagnitudeNumber(-4.0, 4.0) != 4.0) return 4;
    return 0;
}
```

The operations are allocation-free and `nothrow`. Selective builds need
`Numerics/Math.gf`. Existing `Min`, `Max`, `MinMagnitude`, and `MaxMagnitude`
continue to propagate NaN when either input is NaN.
