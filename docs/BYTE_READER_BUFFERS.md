# Caller-owned byte-reader buffers

Load `IO/byte_reader_byte_buffers.gf` alongside `IO/byte_reader.gf` and
`IO/byte_reader_read_at_least.gf` for byte-pointer overloads on `IByteReader*`:

- `ReadExactly(byte* data, nuint count)`
- `ReadAtLeast(byte* data, nuint capacity, nuint minimumBytes, bool throwOnEndOfStream = true)`
- `CopyTo(ITextWriter* output, byte* scratch, nuint capacity)`

```csharp
using Standard::IO;

struct Input : IByteReader
{
    public nuint Read(char* data, nuint count)
    {
        for (nuint i = 0; i < count; i++) data[i] = (char)255;
        return count;
    }
}

int main()
{
    Input input = new();
    IByteReader* reader = &input;
    byte[3] bytes;
    reader.ReadExactly(&bytes[0], 3);
    if (bytes[0] != (byte)255 || bytes[2] != (byte)255) return 1;
    return 0;
}
```

These overloads preserve the character-buffer helpers' contracts. Exact reads
throw on early EOF. Minimum reads can return a short count when
`throwOnEndOfStream` is false. Zero-length reads still validate the source with a
zero-byte request. Errors may leave consumed input and a written output prefix.
The buffers must be independent of reader state and backing storage.

Copying requires nonempty scratch storage independent of source and sink. It
returns the copied byte count and leaves both endpoints open. The helpers do not
allocate buffers, though the source or destination may allocate.

Free-function forms are also available under `Standard::IO`. These overloads
apply to the borrowed reader interface. A concrete type's own methods may offer
a different overload set.
