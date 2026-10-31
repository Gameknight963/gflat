# Binary scalar IO

Import `Standard::IO` to read fixed-width values through a borrowed `IByteReader*` and
write them through a borrowed `ITextWriter*`. The endpoints can be a `File`, an
optional `Windows::TcpClient`, or an application implementation of the contracts.
No owning File/TcpClient value is copied or transferred.

Each method explicitly selects byte order.

| Name component | Type | Bytes |
| --- | --- | --- |
| `UInt16` / `Int16` | `ushort` / `short` | 2 |
| `UInt32` / `Int32` | `uint` / `int` | 4 |
| `UInt64` / `Int64` | `ulong` / `long` | 8 |
| `ExtraLong` | `extralong` | 16 |
| `Float` / `Double` | `float` / `double` | 4 / 8 |

Every row supports `Read<Name>LittleEndian`, `Read<Name>BigEndian`,
`Write<Name>LittleEndian`, and `Write<Name>BigEndian`. For example,
`input.ReadUInt32BigEndian()` returns `uint`. `output.WriteDoubleLittleEndian(value)`
sends a `double`. All 36 names match the established `FileBinary` operations.

Signed integers preserve their two's-complement bits. Floating operations preserve
IEEE-754 bits, including signed zero, subnormals, infinities, and NaN signs/payloads.
Codecs operate on bytes and do not depend on host endianness, native alignment,
pointer width or C `long` width.

## Complete file example

This program writes two values to the supplied path, closes the file, and reads them
back through the same borrowed interfaces usable by a network consumer. Opening the
output truncates an existing file at that path.

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
        File output = File::OpenWrite(argv[1]);
        WriteLength(&output, 42u);
        WriteDoubleLittleEndian(&output, -1.5);
        output.Close();
    }
    {
        File input = File::OpenRead(argv[1]);
        if (ReadLength(&input) != 42u) return 3;
        if (ReadDoubleLittleEndian(&input) != -1.5) return 4;
        input.Close();
    }
    return 0;
}
```

For an open `Windows::TcpClient client`, `ReadLength(&client)` has the same signature
and representation. It waits for the four-byte prefix, not connection EOF. The caller
must enforce its own payload limits and read the declared body separately.

## Progress, lifetime and failure

Readers use the existing [borrowed reader](BYTE_READERS.md) `ReadExactly` loop. Short
positive reads continue. EOF before the scalar width throws `EndOfStreamException`.
A final zero-byte result may require one further read after a short positive prefix.
Reader exceptions propagate unchanged and may have consumed an unknown prefix.
There is no rewind or rollback, and a failed read returns no scalar.

Writers build a 2/4/8/16-byte stack buffer and call the synchronous sink's `Write`
exactly once. A sink exception can follow partial output. No retry or rollback is
attempted. These operations never retain endpoints, allocate an intermediate buffer,
flush, close or finalize them. Keep each concrete owner alive, unmoved and usable
throughout the call. Underlying sources/sinks and exception paths can allocate.

`FileBinary::Read...` / `FileBinary::Write...` remain available with their existing
`File*` signatures as thin forwarding conveniences. Core extensions do not require
libc. Use [File varints](FILE_VARINT.md) for canonical variable-length integers.

## Explicit source selection

The source files are `core/IO/byte_reader_binary.gf` and `core/IO/text_writer_binary.gf`.
Both reuse `core/Binary/BinaryPrimitives.gf`, whose own methods are self-contained
`nothrow` byte operations with no allocator or exception dependency.

The reader additionally needs `core/IO/IByteReader.gf`, `core/IO/byte_reader.gf`,
`core/IO/IOException.gf`, `core/IO/EndOfStreamException.gf`, and the project's normal
`ITextWriter`/`Exception` definitions and exception runtime support. `byte_reader.gf`
also defines copying to writers, so its `ITextWriter` contract is still required.
The writer-only file needs `BinaryPrimitives` and the existing `ITextWriter` contract.
It does not require the reader interface/helper files.

Existing manual `FileBinary` source lists must now include both extension files,
`BinaryPrimitives`, and the borrowed reader helper dependencies above in addition to
`libc/IO/File.gf` and `libc/IO/FileBinary.gf`. Ordinary standard-library builds include
these source files automatically. No GC or additional native library is introduced.
