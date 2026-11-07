# Rounding to decimal places

`Math::Round(value, digits)` rounds a `float` or `double` to the requested number
of fractional decimal digits, using ties-to-even. Supply `MidpointRounding` as
the third argument to choose another rounding mode.

```csharp
using Standard::Numerics;

int main()
{
    if (Math::Round(12.125, 2) != 12.12) return 1;
    if (Math::Round(12.125, 2, MidpointRounding::AwayFromZero) != 12.13) return 2;
    if (Math::Round(-12.125f, 2, MidpointRounding::ToNegativeInfinity) != -12.13f) return 3;
    return 0;
}
```

## Inputs and results

`digits` must be between 0 and 6 for `float`, or between 0 and 15 for `double`.
These are fractional places, not significant digits. Zero digits rounds to an
integer represented by the same floating-point type. Invalid digit counts or
rounding modes throw, including when the input is NaN or infinity. Valid calls
allocate no storage.

NaN remains NaN, infinities remain unchanged, and results that round to zero
preserve the input's sign. Values with magnitude at least 100,000,000 for `float`
or 10,000,000,000,000,000 for `double` are returned unchanged.

Rounding scales the binary floating-point value by a power of ten, rounds it,
and scales back. Decimal literals are not always represented exactly, and scaling
can introduce another rounding step. A value that looks like a decimal midpoint
can therefore fall to either side of the midpoint. This operation does not
provide decimal arithmetic or an exact decimal representation.

The overloads are in `Numerics/Math.gf`. See [rounding modes](MIDPOINT_ROUNDING.md)
for the five supported modes, and [Rational](RATIONAL.md) for exact rational
arithmetic.
