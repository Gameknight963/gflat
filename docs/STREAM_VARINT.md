# Borrowed stream varints

Import `Standard::IO` for variable-length integers on `IByteReader*` and
`ITextWriter*`:

| Read | Write | Maximum bytes |
| --- | --- | --- |
| `ReadVarUInt32()` | `WriteVarUInt32(uint value)` | 5 |
| `ReadVarUInt64()` | `WriteVarUInt64(ulong value)` | 10 |
| `ReadZigZagInt32()` | `WriteZigZagInt32(int value)` | 5 |
| `ReadZigZagInt64()` | `WriteZigZagInt64(long value)` | 10 |

The encoding is canonical unsigned base-128. Signed methods apply the
[ZigZag mapping](VARINT.md#signed-values-with-zigzag) first.

## Shared file or TCP framing

This example records a length-prefixed payload. Opening output truncates the file.

```csharp
using Standard::IO;

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

The length reader also accepts an open Windows TcpClient. Check application
limits before reading a declared body. A persistent connection may contain
another message, so consume the declared payload rather than copying to EOF.

## Consumption, failures and cost

Readers request one byte per encoded group and stop at its terminator or width
limit. A direct TcpClient can therefore make five or ten `recv` calls per integer.
Use [BufferedReader](BUFFERED_READER.md) for small-read buffering, or parse a
received block with BinaryBufferReader.

| Failure | Exception |
| --- | --- |
| EOF before or during the integer | `EndOfStreamException` |
| Overlong encoding, overflow, or continuation beyond the width | `IOException` |
| Underlying read/write error | Propagates from the endpoint |

Reads can consume a prefix before failure. Writes send one encoded stack buffer
and can leave partial output if the destination throws. Handle the failed record
before continuing with the stream.

The File-specific [FileVarInt](FILE_VARINT.md) methods use libc's buffered byte IO.

## Explicit sources

Sources: `core/IO/byte_reader_var_int.gf`, `core/IO/text_writer_var_int.gf`,
`core/Binary/VarInt.gf`, and the byte-reader/text-writer contracts.
