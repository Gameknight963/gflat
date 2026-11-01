# Binary scalar IO

Import `Standard::IO` to read fixed-width values from `IByteReader*` and write
them to `ITextWriter*`.

| Name component | Type | Bytes |
| --- | --- | --- |
| `UInt16`, `Int16` | `ushort`, `short` | 2 |
| `UInt32`, `Int32` | `uint`, `int` | 4 |
| `UInt64`, `Int64` | `ulong`, `long` | 8 |
| `ExtraLong` | `extralong` | 16 |
| `Float`, `Double` | `float`, `double` | 4, 8 |

Combine `Read` or `Write`, the name above, and `LittleEndian` or `BigEndian`.
For example, `ReadUInt32BigEndian()` returns `uint`, and
`WriteDoubleLittleEndian(value)` writes a `double`.

Signed values use two's-complement bits. Floating values preserve IEEE 754 bits,
including NaN payloads and signed zero.

## Complete file example

Opening the output truncates the supplied path.

```csharp
using Standard::IO;

uint ReadLength(IByteReader* input)
{
    return input.ReadUInt32BigEndian();
}

void WriteLength(ITextWriter* output, uint value)
{
    output.WriteUInt32BigEndian(value);
}

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    {
        FileStream output = File::OpenWrite(argv[1]);
        WriteLength(&output, 42u);
        WriteDoubleLittleEndian(&output, -1.5);
        output.Close();
    }
    {
        FileStream input = File::OpenRead(argv[1]);
        if (ReadLength(&input) != 42u) return 3;
        if (ReadDoubleLittleEndian(&input) != -1.5) return 4;
        input.Close();
    }
    return 0;
}
```

`ReadLength(&client)` also accepts an open Windows TcpClient. It waits for the
four-byte field. Read the declared body separately, after checking application
size limits.

## Progress, lifetime and failure

Reads continue through short source results until the scalar is complete.
Early EOF throws `EndOfStreamException`. Other reader errors propagate.
Either failure can leave input consumed.

Writes encode the scalar into a stack buffer and make one destination call.
A destination exception can leave partial output. Keep each endpoint alive
and at the same address for the call.

`FileBinary` offers the same operations with `FileStream*` arguments. For a compact
variable-width representation, use [stream varints](STREAM_VARINT.md).

## Explicit source selection

Sources: `core/IO/byte_reader_binary.gf`, `core/IO/text_writer_binary.gf`,
`core/Binary/BinaryPrimitives.gf`, and the byte-reader/text-writer contracts.
