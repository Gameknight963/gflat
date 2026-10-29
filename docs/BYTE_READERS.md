# Borrowed byte readers

`std::IO::IByteReader` is a source of raw counted bytes. `Read(char* data, nuint count)`
returns how many bytes were written into caller storage. Embedded zero and high-bit
bytes have no special meaning. Nothing is decoded or zero-terminated.

For a positive request, zero means EOF. A short positive read is normal and must not
be treated as EOF. This is a blocking protocol: a nonblocking source must not report
"would block" as zero. A zero-byte request consumes nothing and must not dereference
the buffer, but can still reject a closed or otherwise unusable source.

`File` and optional `Windows::TcpClient` implement this interface without transferring
their ownership. Keep each concrete value alive, unmoved and open for every call
through its borrowed interface pointer. The interface does not add closing, seeking,
length or ownership semantics.

## Helpers

Import `std::IO` for these extensions on `IByteReader*`:

- `ReadExactly(data, count)` loops through short reads and throws `EndOfStreamException`
  if EOF arrives before `count`. A zero-byte operation calls the source once to preserve
  its state validation. Reader exceptions propagate unchanged.
- `CopyTo(output, scratch, capacity)` rejects empty scratch, reads until EOF, and sends
  each counted chunk to `ITextWriter*`. It returns the number of bytes sent. A source
  reporting more bytes than requested or a total count overflow throws `IOException`
  before forwarding that chunk.
- `CopyTo(output)` supplies 4096 bytes of stack scratch.

The helpers retain no endpoint or buffer and allocate no intermediate data. They do
not close, flush, rewind, or call an encoder/decoder's `Complete`. Input, output and
scratch must be logically distinct, with no overlap between scratch and either
endpoint's state or retained backing bytes. Do not copy a stream into itself, even
through different adapters. The generic helper cannot establish endpoint identity.
Existing concrete `File::CopyTo` validation remains unchanged.

Failure can leave consumed input, a modified output-buffer prefix, or already-written
chunks. There is no rollback or automatic draining. A reader can fail after consuming
an unknown prefix, so callers cannot infer consumed bytes from an exception alone.

## Decode file or network input through the same consumer

The existing strict `HexDecoder` is an `ITextWriter`. It accepts arbitrary chunk splits
and checks the final tail in `Complete`, so the consumer needs no entire-input string.
Use distinct source and destination files: opening the destination truncates it.

```csharp
using std::IO;
using std::Encoding;

void DecodeHexFrom(IByteReader* input, ITextWriter* output)
{
    HexDecoder decoder = new(output);
    input.CopyTo(&decoder);
    decoder.Complete();
}

void DecodeFile(readonly(char)* sourcePath, readonly(char)* destinationPath)
{
    File source = File::OpenRead(sourcePath);
    File destination = File::OpenWrite(destinationPath);
    DecodeHexFrom(&source, &destination);
    destination.Close();
    source.Close();
}

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    DecodeFile(argv[1], argv[2]);
    return 0;
}
```

With the optional Windows network project referenced, the same `DecodeHexFrom` accepts
`&client` for an open `Windows::TcpClient`. For a TCP stream, its peer must end sending
for EOF-based copying to finish. A persistent connection carrying multiple messages
needs a separate bounded framing protocol; `CopyTo` does not know message boundaries.
