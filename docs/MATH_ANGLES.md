# Mathematical constants and angle conversion

`Math::E`, `Math::PI`, and `Math::Tau` are double-precision constants. Tau is
one full turn in radians. `DegreesToRadians` and `RadiansToDegrees` have both
float and double overloads and preserve the argument's floating-point type.

```csharp
using Standard::Numerics;

int main()
{
    double radians = Math::DegreesToRadians(180.0);
    if (Math::Abs(radians - Math::PI) > 0.000000000000001) return 1;
    float degrees = Math::RadiansToDegrees((float)Math::PI);
    if (Math::Abs(degrees - 180.0f) > 0.0001f) return 2;
    return 0;
}
```

Conversion order matches the [.NET 10 implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Double.cs):
degrees are multiplied by PI and then divided by 180, while radians are
multiplied by 180 and then divided by PI. Each operation uses the selected
precision. Intermediate multiplication can overflow even when the exact
converted value would fit. The methods do not normalize angles to a turn.
Normal floating-point rounding and underflow also apply. Signed zero retains its sign,
infinities retain their sign, and NaN remains NaN. NaN payload preservation is
not promised. A round trip can lose precision.

These operations are allocation-free and `nothrow`. Selective builds need
`Numerics/Math.gf`, with no hosted math library dependency.
