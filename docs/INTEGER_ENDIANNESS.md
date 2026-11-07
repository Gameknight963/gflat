# Reversing integer byte order

`BitOperations::ReverseEndianness(value)` reverses the bytes of an integer,
independently of host byte order. It accepts `byte`, `sbyte`, `short`, `ushort`,
`int`, `uint`, `long`, `ulong`, `nint`, `nuint`, and `extralong`. The return type
matches the argument type. One-byte values are unchanged.

```csharp
using Standard::Numerics;

int main()
{
    extralong value = ((extralong)0x0102030405060708ul << 64) | (extralong)0x090a0b0c0d0e0f10ul;
    extralong reversed = BitOperations::ReverseEndianness(value);
    if ((ulong)reversed != 0x0807060504030201ul) return 1;
    if ((ulong)(reversed >> 64) != 0x100f0e0d0c0b0a09ul) return 2;
    if (BitOperations::ReverseEndianness(reversed) != value) return 3;
    return 0;
}
```

Native-width overloads reverse all bytes in the target's pointer-sized integer.
The 128-bit overload reverses all sixteen bytes, including exchanging the two
64-bit halves. Signed values are treated as bit patterns. All overloads are
`nothrow` and need no allocation or native runtime helpers.

The scalar behavior matches [.NET BinaryPrimitives.ReverseEndianness](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.binary.binaryprimitives.reverseendianness).
