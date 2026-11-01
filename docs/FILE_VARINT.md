# Canonical variable-length integers in files

`Standard::IO::FileVarInt` reads and writes canonical [base-128 integers](VARINT.md)
at a FileStream's current position.

| Read | Write | Maximum bytes |
| --- | --- | --- |
| `ReadUInt32(FileStream*)` | `WriteUInt32(FileStream*, uint)` | 5 |
| `ReadUInt64(FileStream*)` | `WriteUInt64(FileStream*, ulong)` | 10 |

```csharp
using Standard;
using Standard::IO;
using Standard::Binary;

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    {
        FileStream output = File::OpenWrite(argv[1]);
        FileVarInt::WriteUInt32(&output, 3u);
        FileVarInt::WriteUInt64(&output, VarInt::ZigZagEncode(-42l));
        output.Close();
    }
    FileStream input = File::OpenRead(argv[1]);
    uint count = FileVarInt::ReadUInt32(&input);
    long signedValue = VarInt::ZigZagDecode(FileVarInt::ReadUInt64(&input));
    input.Close();
    if (count != 3u || signedValue != -42l) return 1;
    return 0;
}
```

Readers stop at the integer's terminator or width limit, leaving the next field
unread. Empty input or truncated integers throw `EndOfStreamException`.
Noncanonical encodings, overflow, closed files, and IO errors throw `IOException`.
Failure can leave part of an integer consumed.

Writes send one counted encoded buffer. Close or flush the file explicitly to
observe delayed libc output errors.

Use `VarInt::ZigZagEncode` and `ZigZagDecode` for signed values, as above. The
[stream extensions](STREAM_VARINT.md) offer the same encoding on any IByteReader
or ITextWriter.

Source: `libc/IO/FileVarInt.gf`
