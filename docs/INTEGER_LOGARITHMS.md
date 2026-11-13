# Integer base-two logarithms

`Standard::Numerics` provides two integer-valued operations for working with
powers of two. All overloads return `int` and are `nothrow`.

| Operation | Inputs | Result |
| --- | --- | --- |
| `BitOperations::Log2(value)` | `uint`, `ulong`, `nuint` | Floor of the base-two logarithm, with zero returning zero |
| `Math::ILogB(value)` | `float`, `double` | Binary exponent of the absolute value, with the special results below |

For a positive integer, `Log2` gives the index of its highest set bit, counting
from zero. For example, 8 through 15 return 3. Its zero result is a convention,
so test for a nonzero input when zero must be distinguished from one.

For finite nonzero floating-point input, `ILogB` returns the unique exponent
`e` for which `2^e <= abs(value) < 2^(e+1)`. Negative values have the same
result as their positive counterparts. Values below one have negative
exponents, including subnormal values.

| `ILogB` input | Result |
| --- | --- |
| Either signed zero | Minimum int, -2147483648 |
| Either infinity or any NaN | Maximum int, 2147483647 |
| Smallest positive float subnormal | -149 |
| Smallest positive double subnormal | -1074 |

```csharp
using Standard::Binary;
using Standard::Numerics;

int main()
{
    if (BitOperations::Log2(1024u) != 10 || BitOperations::Log2(1023ul) != 9) return 1;
    if (BitOperations::Log2((nuint)0) != 0) return 2;
    if (Math::ILogB(0.125) != -3 || Math::ILogB(-12.0f) != 3) return 3;
    uint bits = 1u;
    if (Math::ILogB(BitConverter::UInt32BitsToFloat(bits)) != -149) return 4;
    return 0;
}
```

These operations do not allocate or call libc. They use integer bit operations
and inspect floating-point representations directly, so logarithm rounding
cannot move a value across a power-of-two boundary.

Selective builds include `Numerics/BitOperations.gf` for `Log2` or
`Numerics/Math.gf` for `ILogB`.
