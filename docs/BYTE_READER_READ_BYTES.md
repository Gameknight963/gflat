# Reading a bounded owning byte buffer

Load `IO/byte_reader_read_bytes.gf` for `IByteReader.ReadBytes(count)`. It returns
an owning `Vector<byte>` with up to `count` bytes from the current cursor.
Short reads continue until the count is reached or the source returns EOF.
Early EOF returns the available bytes rather than throwing. The source stays open.

```csharp
using Standard::Collections;
using Standard::IO;

struct Input : IByteReader
{
    private nuint position;
    public Input() { position = 0; }
    public nuint Read(char* output, nuint count)
    {
        if (position == 3 || count == 0) return 0;
        output[0] = (char)((int)'a' + (int)position);
        position++;
        return 1;
    }
}

int main()
{
    Input input = new();
    IByteReader* reader = &input;
    Vector<byte> first = reader.ReadBytes(2);
    Vector<byte> rest = reader.ReadBytes(2);
    if (first.Count != 2 || first[0] != (byte)'a' || first[1] != (byte)'b') return 1;
    if (rest.Count != 1 || rest[0] != (byte)'c') return 2;
    return 0;
}
```

The method does not read beyond the requested count or probe for EOF afterward.
A zero count makes a zero-byte read to validate source state and returns an empty
vector without allocating its storage. The buffer grows as bytes arrive rather
than reserving the entire requested count in advance. Capacity can exceed Count.

Reader errors, invalid returned counts, and allocation failures throw and release
the temporary buffer. Some input may already have been consumed when an error
occurs. Use `ReadExactly` with a caller-owned buffer when early EOF must throw.

The bounded-read and early-EOF behavior follows
[BinaryReader.ReadBytes](https://learn.microsoft.com/en-us/dotnet/api/system.io.binaryreader.readbytes).
