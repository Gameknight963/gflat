# Taking an owning memory-stream snapshot

`MemoryStream.ToBytes()` returns a new `Vector<byte>` containing the stream's
entire contents. It includes bytes before the cursor and leaves `Position`
unchanged, even when the cursor is beyond the end.

```csharp
using Standard::IO;
using Standard::Collections;

int main()
{
    MemoryStream stream = new();
    stream.Write("a\0b", 3);
    stream.Position = 2l;
    Vector<byte> bytes = stream.ToBytes();
    if (bytes.Count != 3 || bytes[1] != (byte)0 || stream.Position != 2l) return 1;
    bytes[0] = (byte)'x';
    stream.Position = 0l;
    if (stream.ReadByte() != (int)'a') return 2;
    return 0;
}
```

The result owns an independent byte-for-byte copy. It remains valid after the
stream is changed, cleared, or destroyed. Unused source capacity is not copied.
An empty stream returns an empty vector without allocating. The method accepts
an immutable stream and takes time proportional to its length.

A nonempty snapshot allocates its own storage. Allocation failure leaves the
stream, its cursor, and an assignment destination unchanged. To copy into a
writer instead, use [WriteTo](MEMORY_STREAM_WRITE_TO.md). To borrow storage
without copying, use `AsSpan` or `AsReadOnly` and observe their storage lifetime.
