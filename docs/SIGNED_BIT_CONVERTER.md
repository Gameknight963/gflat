# Signed representation bits and native byte order

`BitConverter::FloatToInt32Bits` and `DoubleToInt64Bits` return the original
representation bits as `int` and `long`. `Int32BitsToFloat` and
`Int64BitsToDouble` restore those bits. A set high bit makes the integer
negative without changing its representation. These are bit reinterpretations,
not numeric conversions.

```csharp
using Standard::Binary;

int main()
{
    int bits = BitConverter::FloatToInt32Bits(-0.0f);
    if (bits != -2147483647 - 1) return 1;
    float value = BitConverter::Int32BitsToFloat(bits);
    if (BitConverter::FloatToUInt32Bits(value) != 0x80000000u) return 2;
    long doubleBits = BitConverter::DoubleToInt64Bits(-1.0);
    if (BitConverter::Int64BitsToDouble(doubleBits) != -1.0) return 3;
    return 0;
}
```

The methods preserve signed zeros, subnormals, infinities, and NaN payloads.
Subsequent floating-point arithmetic may change a NaN representation. All
operations are allocation-free and `nothrow`.

`BitConverter::IsLittleEndian` reports whether the running program's native
integer representation stores the least significant byte first. It does not
select a file or network byte order. Use the explicit-endian
[BinaryPrimitives](BINARY_PRIMITIVES.md) operations for those formats.

Selective builds need `Binary/BitConverter.gf`. The operations require no
Standard allocation or exception runtime. Freestanding Windows executables
using floating-point values still need the usual `_fltused` startup marker.
