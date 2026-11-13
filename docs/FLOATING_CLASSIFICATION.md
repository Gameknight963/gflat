# Classifying floating-point values

`Standard::Numerics::Math` has matching `float` and `double` overloads for these
`nothrow` predicates:

| Method | Returns true for |
| --- | --- |
| `IsNaN` | Any NaN |
| `IsFinite` | Normal values, subnormal values, and either zero |
| `IsInfinity` | Either infinity |
| `IsPositiveInfinity`, `IsNegativeInfinity` | The specified infinity |
| `IsNormal` | Finite, nonzero values with a normal exponent |
| `IsSubnormal` | Nonzero values between zero and the smallest normal magnitude |
| `IsNegative` | Values with the sign bit set, including negative zero and negative NaNs |
| `IsPositive` | Values with the sign bit clear, including positive zero and positive NaNs |

The sign predicates inspect the representation. Use `value < 0` or `value > 0`
for an ordered numeric comparison instead. `Math::Sign` returns zero for either
zero and throws for NaN.

```csharp
using Standard::Binary;
using Standard::Numerics;

int main()
{
    uint zeroBits = 0x80000000u;
    float negativeZero = BitConverter::UInt32BitsToFloat(zeroBits);
    if (!Math::IsNegative(negativeZero) || Math::IsPositive(negativeZero)) return 1;
    if (!Math::IsFinite(negativeZero) || Math::IsNormal(negativeZero)) return 2;
    if (Math::Sign(negativeZero) != 0) return 3;

    uint tinyBits = 1u;
    float tiny = BitConverter::UInt32BitsToFloat(tinyBits);
    if (!Math::IsSubnormal(tiny) || Math::IsNormal(tiny)) return 4;

    ulong infinityBits = 0x7ff0000000000000ul;
    double infinity = BitConverter::UInt64BitsToDouble(infinityBits);
    if (!Math::IsInfinity(infinity) || !Math::IsPositiveInfinity(infinity)) return 5;
    if (Math::IsFinite(infinity) || Math::IsNaN(infinity)) return 6;
    return 0;
}
```

Classification does not allocate or call libc. Normal, subnormal, infinity,
and sign-bit predicates inspect the IEEE representation directly, including
NaNs with different payloads and signaling bits.
