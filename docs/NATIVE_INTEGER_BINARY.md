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

## Cursor-based buffers

`BinaryBufferReader` and `BinaryBufferWriter` expose the same four read and
write methods without pointer/count arguments. Success advances Position by
`sizeof(nint)`. Insufficient remaining capacity returns false without advancing
Position. Failed reads also clear the result to zero, and failed writes leave
all bytes unchanged. Default empty cursors fail without accessing storage.
Copies retain independent positions over the same borrowed bytes.

```csharp
using Standard::Binary;

int main()
{
    byte[16] storage;
    BinaryBufferWriter writer = new(&storage[0], 16);
    if (!writer.TryWriteNIntBigEndian((nint)-123)) return 1;
    BinaryBufferReader reader = new(&storage[0], writer.Position);
    nint value = 0;
    if (!reader.TryReadNIntBigEndian(ref value) || value != (nint)-123) return 2;
    if (reader.Remaining != 0) return 3;
    return 0;
}
```

Cursor builds additionally include `Binary/BinaryBufferReader.gf`,
`Binary/BinaryBufferWriter.gf`, `IO/IByteReader.gf`, and `Memory/bytes.gf`.
Native-width cursor operations are allocation-free and `nothrow`.
