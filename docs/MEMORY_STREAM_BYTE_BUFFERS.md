# Use byte buffers with a memory stream

`MemoryStream.Read(byte* data, nuint count)`,
`Write(readonly(byte)* data, nuint count)`, and `WriteByte(byte value)` work
with binary buffers directly. The existing `char` overloads remain available.

```csharp
using Standard::IO;

int main()
{
    MemoryStream stream = new();
    byte[3] input = [(byte)0, (byte)128, (byte)255];
    stream.Write(&input[0], 3);
    stream.WriteByte((byte)42);
    stream.Position = 0l;
    byte[4] output;
    if (stream.Read(&output[0], 4) != 4) return 1;
    if (output[0] != (byte)0 || output[1] != (byte)128 || output[2] != (byte)255 || output[3] != (byte)42) return 2;
    return 0;
}
```

Reads return the available byte count, advance Position by that amount, and
return zero at or beyond the end. Zero-count operations do not access the
buffer or change stream state.

Writes can borrow the stream's existing contents, including overlapping ranges
and writes that grow the buffer. Seeking past the end and then writing fills
the gap with zero bytes. An allocation failure preserves contents, Length,
Position, and Capacity.

Selective builds use the normal [memory stream dependencies](../Standard/core/IO/MEMORY_STREAM.md).
These overloads do not change the `IByteReader` or `ITextWriter` interfaces.
