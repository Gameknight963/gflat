# Floating-point bounds and magnitudes

`Math` provides `Clamp`, `MinMagnitude`, and `MaxMagnitude` overloads for
`float` and `double`. Load `Standard/core/Numerics/Math.gf`.

```csharp
using Standard::Numerics;

int main()
{
    if (Math::Clamp(1.5, 0.0, 1.0) != 1.0) return 1;
    if (Math::MinMagnitude(-3.0, 2.0) != 2.0) return 2;
    if (Math::MaxMagnitude(-3.0f, 2.0f) != -3.0f) return 3;
    if (Math::MinMagnitude(-2.0, 2.0) != -2.0) return 4;
    return 0;
}
```

`Clamp(value, minimum, maximum)` returns the nearest bound when `value` lies
outside the inclusive interval. It throws when `minimum > maximum`.
A NaN value stays NaN. A NaN bound does not compare less or greater and
therefore does not constrain that side of the interval. A value already
within the bounds is returned with its original sign, including negative zero.

The magnitude methods return one of their inputs, retaining its sign.
`MinMagnitude` selects the smaller absolute value and `MaxMagnitude` the larger.
On equal magnitudes, minimum favors the negative value and maximum favors the
positive value, including signed zero and opposite infinities. If either input
is NaN, the result is NaN. NaN payload propagation is not specified.
Both magnitude methods are `nothrow`. Successful calls do not allocate.
