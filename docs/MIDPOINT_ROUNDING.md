# Choosing a rounding mode

`Math::Round(value, MidpointRounding mode)` accepts `float` or `double`.
The enum and overloads are included in `Standard/core/Numerics/Math.gf`.

```csharp
using Standard::Numerics;

int main()
{
    if (Math::Round(2.5, MidpointRounding::ToEven) != 2.0) return 1;
    if (Math::Round(2.5, MidpointRounding::AwayFromZero) != 3.0) return 2;
    if (Math::Round(-2.1, MidpointRounding::ToNegativeInfinity) != -3.0) return 3;
    if (Math::Round(-2.9f, MidpointRounding::ToZero) != -2.0f) return 4;
    return 0;
}
```

| Mode | Result |
| --- | --- |
| `ToEven` | Nearest integer, choosing the even integer at a midpoint |
| `AwayFromZero` | Nearest integer, choosing the greater magnitude at a midpoint |
| `ToZero` | Truncate the fractional part for every input |
| `ToNegativeInfinity` | Round down for every input |
| `ToPositiveInfinity` | Round up for every input |

The directed modes apply to every fractional value, not just midpoints.
The overload without a mode remains ties-to-even. Negative zero is preserved
when rounding produces zero. NaN remains NaN and infinities remain unchanged.
Invalid enum values throw before a result is returned, including for nonfinite
inputs. Successful calls allocate no storage.

Use `Math::Round(value, digits, mode)` to apply the same modes to fractional
decimal places. See [rounding to decimal places](DECIMAL_ROUNDING.md) for digit
limits and binary floating-point behavior.
