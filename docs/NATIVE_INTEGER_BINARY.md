# Read and write native-sized integers

`BinaryPrimitives` provides `TryReadNIntLittleEndian`, `TryReadNIntBigEndian`,
`TryReadNUIntLittleEndian`, and `TryReadNUIntBigEndian`, with matching `TryWrite`
methods. They transfer exactly `sizeof(nint)` bytes, using the byte order named
by the method. Signed values preserve their two's-complement representation.

```csharp
using Standard::Binary;

int main()
{
    byte[16] storage;
    nint value = (nint)-123;
    if (!BinaryPrimitives::TryWriteNIntBigEndian(&storage[1], 15, value)) return 1;
    nint restored = 0;
    if (!BinaryPrimitives::TryReadNIntBigEndian(&storage[1], 15, ref restored)) return 2;
    if (restored != value) return 3;
    return 0;
}
```

Buffers may be unaligned. Reads return false and write zero to the result when
count is too short. Writes return false without modifying the buffer. Short
operations do not access buffer bytes. Counts larger than the required width
are accepted, with trailing bytes ignored or left unchanged.

These operations are allocation-free and `nothrow`. The result reference for a
read must refer to initialized writable native-integer storage.

Native width varies between targets. Use explicit Int32, UInt32, Int64, or
UInt64 codecs when a persisted or network format needs a fixed width.
Selective builds need `Binary/BinaryPrimitives.gf`.
