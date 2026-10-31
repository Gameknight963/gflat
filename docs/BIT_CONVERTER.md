# Floating-point representation

`Standard::Binary::BitConverter` converts between floating-point values and
their IEEE representation bits:

| Function | Result |
| --- | --- |
| `FloatToUInt32Bits(float)` | 32 representation bits as `uint` |
| `UInt32BitsToFloat(uint)` | `float` with those bits |
| `DoubleToUInt64Bits(double)` | 64 representation bits as `ulong` |
| `UInt64BitsToDouble(ulong)` | `double` with those bits |

```csharp
using Standard::Binary;

uint negativeZero = BitConverter::FloatToUInt32Bits(-0.0f); // 0x80000000
float restored = BitConverter::UInt32BitsToFloat(negativeZero);
```

For example, `1.0f` has bits `0x3f800000`, while numeric `(uint)1.0f` is one.
These methods preserve signed zero, subnormals, infinities, and NaN payloads.
Later floating-point arithmetic can change a NaN's bits.

Use [BinaryPrimitives](BINARY_PRIMITIVES.md) to write those bits with explicit
byte order. Freestanding Windows builds need the usual
[`_fltused` startup marker](FREESTANDING_CORE.md#windows-floating-point-marker).

Source: `core/Binary/BitConverter.gf`
