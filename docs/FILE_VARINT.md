# Canonical variable-length integers in files

The optional `libc/IO/FileVarInt.gf` connects `File` to the existing
allocation-free `VarInt` codec. Values use canonical unsigned base-128 encoding:
least-significant seven-bit group first, with bit 7 indicating another byte.
These are not fixed-width little-endian integers.

| API | Value width |
| --- | --- |
| `FileVarInt::ReadUInt32(File* input)` | 32 bits, at most 5 bytes |
| `FileVarInt::ReadUInt64(File* input)` | 64 bits, at most 10 bytes |
| `FileVarInt::WriteUInt32(File* output, uint value)` | 32 bits |
| `FileVarInt::WriteUInt64(File* output, ulong value)` | 64 bits |

This complete program writes an unsigned field followed by a ZigZag-encoded signed value,
closes the output, then reads the same records from the path supplied on the
command line:

```csharp
using Standard; using Standard::IO; using Standard::Binary;

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    {
        File output = File::OpenWrite(argv[1]);
        FileVarInt::WriteUInt32(&output, 3u);
        FileVarInt::WriteUInt64(&output, VarInt::ZigZagEncode(-42l));
        output.Close();
    }
    File input = File::OpenRead(argv[1]);
    uint count = FileVarInt::ReadUInt32(&input);
    long signedValue = VarInt::ZigZagDecode(FileVarInt::ReadUInt64(&input));
    input.Close();
    if (count != 3u || signedValue != -42l) return 1;
    return 0;
}
```

Every operation borrows an already open File at its current position. It never
closes, rewinds, flushes, or clears the stream. It retains no reference to it.
Reads stop at the first byte without a continuation bit, or at the width limit.
The following record byte is never read, including after an invalid integer.

An empty stream or an incomplete integer ending at EOF throws
`EndOfStreamException`. A noncanonical overlong integer, width overflow, or
continuation at the maximum byte count throws `IOException`. A closed File or
underlying IO failure also throws `IOException`. Reads can consume a prefix
before failure. There is no rollback or recovery promise after malformed or
truncated data. Byte position guarantees do not imply the remainder is valid.

Writes emit the canonical representation in one counted `File.Write` call.
Buffered host IO can report failures later, so the owner remains responsible for
`Flush`/`Close` when durability or observing a buffered error matters.

Normal operations use at most ten stack bytes for encoded input/output, with no
application allocator call. libc File buffering and exception storage remain
separate. Signed values can compose the existing `VarInt::ZigZagEncode` and
`ZigZagDecode` methods.

Explicit source selection needs `IO/FileVarInt.gf`, `IO/File.gf`, `Binary/VarInt.gf`, and
ordinary File exception/runtime dependencies. The core VarInt codec stays
independent of IO, allocation, and libc.
