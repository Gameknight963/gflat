# Reversing integer byte order

`BinaryPrimitives::ReverseEndianness(value)` swaps the positions of an integer's
bytes without changing the bit order within each byte. Load
`Standard/core/Binary/BinaryPrimitives.gf`.

```csharp
using Standard::Binary;

int main()
{
    uint value = 0x12345678u;
    if (BinaryPrimitives::ReverseEndianness(value) != 0x78563412u) return 1;
    short signedValue = (short)0x8001;
    if (BinaryPrimitives::ReverseEndianness(signedValue) != (short)0x0180) return 2;
    if (BinaryPrimitives::ReverseEndianness(BinaryPrimitives::ReverseEndianness(value)) != value) return 3;
    return 0;
}
```

Overloads cover `byte`, `sbyte`, `ushort`, `short`, `uint`, `int`, `ulong`,
`long`, `nuint`, `nint`, and `extralong`. One-byte inputs are returned unchanged.
Native integers use the target pointer width. Signed types swap their complete
two's-complement representation, including the sign byte. Reversing twice
returns the original value.

The operation is independent of the machine's byte order. It always swaps,
so use the explicitly ordered read/write methods when accessing serialized
buffers. All overloads are `nothrow` and allocate no storage.

See [Reversing byte order in buffers](REVERSE_ENDIANNESS_BUFFERS.md) for overlapping spans and pointer/count buffers.
