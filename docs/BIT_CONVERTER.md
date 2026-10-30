# Floating-point representation

`Standard::Binary::BitConverter` reinterprets IEEE binary32 and binary64 representations:

| Function | Result |
| --- | --- |
| `FloatToUInt32Bits(float value)` | The 32 representation bits as `uint`. |
| `UInt32BitsToFloat(uint bits)` | A `float` with exactly those bits. |
| `DoubleToUInt64Bits(double value)` | The 64 representation bits as `ulong`. |
| `UInt64BitsToDouble(ulong bits)` | A `double` with exactly those bits. |

```csharp
using Standard::Binary;
uint negativeZero = Standard::Binary::BitConverter::FloatToUInt32Bits(-0.0f); // 0x80000000
float restored = Standard::Binary::BitConverter::UInt32BitsToFloat(negativeZero);
```

These are bit reinterpretations, rather than numeric casts. For example,
converting `1.0f` to its bits produces `0x3f800000`, whereas `(uint)1.0f`
produces the integer one. The functions preserve signed zero, subnormals,
infinities, and quiet or signaling NaN payloads. They perform no floating-point
arithmetic; later arithmetic on the resulting value can change a NaN's bits.

Every function is `nothrow`, allocates nothing, and has no other Standard dependency.
Include `core/Binary/BitConverter.gf` independently. It is separate from
`BinaryPrimitives.gf`, which can serve integer-only programs without introducing
floating-point ABI requirements. On Windows, floating-point code can still need
the toolchain's `_fltused` marker; lack of Standard dependencies does not remove
platform ABI requirements.

The returned integers are not serialized byte arrays. Use explicit byte-order
operations when writing their bits into files or packets.
