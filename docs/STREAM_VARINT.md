# Borrowed stream varints

Import `std::IO` for canonical variable-length integer extensions on borrowed
`IByteReader*` / `ITextWriter*` endpoints. The endpoints can be a File, the optional
Windows TcpClient, or a caller-defined reader/writer. No owner is copied or transferred.

| Read operation | Write operation | Value and maximum encoded size |
| --- | --- | --- |
| `ReadVarUInt32()` | `WriteVarUInt32(uint value)` | UInt32, five bytes |
| `ReadVarUInt64()` | `WriteVarUInt64(ulong value)` | UInt64, ten bytes |
| `ReadZigZagInt32()` | `WriteZigZagInt32(int value)` | Int32 via ZigZag, five bytes |
| `ReadZigZagInt64()` | `WriteZigZagInt64(long value)` | Int64 via ZigZag, ten bytes |

These reuse the existing [VarInt](VARINT.md) codec. Unsigned integers use
least-significant seven-bit groups first, with bit 7 indicating continuation. The
shortest representation is required: overlong zero groups, overflow and continuation
at the maximum width throw `IOException`. Signed operations use the existing ZigZag
mapping, not signed LEB128 or protobuf `int32`/`int64` negative representations.
There is no 128-bit varint operation.

## Shared file or TCP framing

This complete program records a length-prefixed three-byte payload and signed metadata,
then reads them back. Opening the output truncates an existing file at the supplied path.
`ReadPayloadLength` can also accept an open `Windows::TcpClient` through `&client` when
its optional network library is referenced, with no change to the parsing code.

```csharp
using std::IO;

uint ReadPayloadLength(IByteReader* input)
{
    uint length = input.ReadVarUInt32();
    if (length > 1048576u) throw new IOException("Payload exceeds application limit");
    return length;
}

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    {
        File output = File::OpenWrite(argv[1]);
        WriteVarUInt32(&output, 3u);
        WriteZigZagInt32(&output, -150);
        output.Write("abc", 3);
        output.Close();
    }
    {
        File input = File::OpenRead(argv[1]);
        if (ReadPayloadLength(&input) != 3u) return 3;
        if (ReadZigZagInt32(&input) != -150) return 4;
        char[3] payload;
        IByteReader* reader = &input;
        reader.ReadExactly(payload, 3);
        if (payload[0] != 'a' || payload[1] != 'b' || payload[2] != 'c') return 5;
        input.Close();
    }
    return 0;
}
```

The payload limit is application policy. A persistent TCP connection need not reach
EOF after a message: read its declared body length rather than copying the connection
until EOF. These helpers provide scalar encoding, not message routing or buffering.

## Consumption, failures and cost

A reader requests exactly one byte per encoded group, stopping at the first terminator
or the width limit. It never prefetches the following field, including after an invalid
integer. Up to five or ten `Read` calls are required. With a direct unbuffered TcpClient,
that means up to five or ten `recv` calls per scalar. No extra buffering is hidden in
these helpers. For bulk parsing, read framed blocks into `BinaryBufferReader` and use
its existing varint adapters, or supply a caller-owned buffered reader.

Empty input or EOF inside an unfinished integer throws `EndOfStreamException`. A
malformed integer throws `IOException` at its terminator or width limit. Underlying
reader exceptions propagate unchanged, possibly after consuming an unknown prefix.
No failed operation rewinds, retries after an exception or guarantees resynchronization.

Each writer encodes into a five- or ten-byte stack buffer and calls the synchronous
sink's `Write` once with the encoded count. Sink exceptions propagate unchanged and
can follow partial output. There is no output rollback. Neither side retains an
endpoint or buffer, closes, flushes or transfers ownership. Keep concrete owners alive,
unmoved and usable throughout the call. Normal helpers allocate no intermediate data;
underlying endpoints and exception handling can allocate.

`FileVarInt` remains unchanged, including its concrete `fgetc`-backed byte path and
four existing unsigned operations. Generic stream extensions do not replace that
buffered file implementation or add more File-specific overloads.

## Explicit sources

Reader extensions live in `core/IO/byte_reader_var_int.gf` and require
`core/Binary/VarInt.gf`, `core/IO/IByteReader.gf`, `core/IO/byte_reader.gf`,
`core/IO/IOException.gf`, `core/IO/EndOfStreamException.gf`, and the existing
`Exception` / `ITextWriter` contracts plus ordinary exception runtime support.
The reader helper source also defines copying to writers, so its writer contract
remains a source dependency even for read-only consumers.

Writer extensions live in `core/IO/text_writer_var_int.gf` and require only the
standalone VarInt codec and existing ITextWriter contract, plus dependencies of the
chosen sink. They do not require IByteReader, BinaryPrimitives, File, a GC or a new
native library. Ordinary standard-library source discovery includes both files.
