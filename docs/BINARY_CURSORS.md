# Borrowed binary buffer cursors

`Standard::Binary::BinaryBufferReader` and `BinaryBufferWriter` track positions
within byte buffers.

```csharp
using Standard;
using Standard::Binary;

byte[16] bytes;
BinaryBufferWriter writer = new(bytes, 16);
writer.TryWriteUInt16BigEndian((ushort)7);
writer.TryWriteInt32LittleEndian(-42);

BinaryBufferReader reader = new(bytes, writer.Position);
ushort tag = 0;
int number = 0;
bool success = reader.TryReadUInt16BigEndian(ref tag)
    && reader.TryReadInt32LittleEndian(ref number);
```

| Member | Meaning |
| --- | --- |
| Reader `Length` | Captured readable byte count |
| Writer `Capacity` | Captured writable byte count |
| `Position` | Current byte offset |
| `Remaining` | Bytes from the cursor to the end |
| `Data` | Start of the captured region, or null for an empty cursor |
| `Reset()` | Sets position to zero |
| `TrySkip(count)` | Advances if enough bytes remain |
| `TrySeek(position)` | Sets an absolute position from zero through the region's end |

Readers expose readonly data. Mutable and shallow readonly writers expose writable
data. Skipping or seeking a writer reserves a position but leaves those bytes
uninitialized. Save the end position before backfilling a header.

Scalar method names follow [BinaryPrimitives](BINARY_PRIMITIVES.md), such as
`TryReadUInt32BigEndian` and `TryWriteFloatLittleEndian`. Successful calls advance
by the scalar width. Insufficient space preserves position, makes reads write zero,
and leaves write buffers unchanged. `TryPeekByte` returns the next byte without
advancing, or false and zero at the end.

## Bounded payloads

| Method | Effect |
| --- | --- |
| `TryReadBytes(output, count)` | Copies the next bytes and advances |
| `TryWriteBytes(input, count)` | Copies into the next bytes and advances |
| `TryReadSlice(count, ref slice)` | Advances the parent and returns a bounded reader |
| `TryWriteSlice(count, ref slice)` | Advances the parent and returns a bounded writer |

Byte copies allow overlapping buffers in either direction. Insufficient space
preserves position and destination bytes. A failed slice request preserves parent
position and resets the output child to empty. Empty slices have null `Data`.
Writer slices leave their contents uninitialized.

Keep backing storage alive for all cursors and children. Copies and children
share bytes but have independent positions. Keep cursor objects and slice outputs
separate from the backing buffers and parent metadata. Scalar read results may
overlap aligned input storage, which then changes after the result store.

## Optional variable-length integers

```csharp
using Standard::Binary;

extension BinaryBufferWriter {
    public bool TryWriteVarUInt32(uint value) nothrow;
}
extension BinaryBufferWriter {
    public bool TryWriteVarUInt64(ulong value) nothrow;
}
extension BinaryBufferReader {
    public VarInt::Status ReadVarUInt32(ref uint value) nothrow;
}
extension BinaryBufferReader {
    public VarInt::Status ReadVarUInt64(ref ulong value) nothrow;
}
extension BinaryBufferWriter {
    public bool TryWriteZigZagInt32(int value) nothrow;
}
extension BinaryBufferWriter {
    public bool TryWriteZigZagInt64(long value) nothrow;
}
extension BinaryBufferReader {
    public VarInt::Status ReadZigZagInt32(ref int value) nothrow;
}
extension BinaryBufferReader {
    public VarInt::Status ReadZigZagInt64(ref long value) nothrow;
}
```

The varint extensions use [canonical base-128 encoding](VARINT.md). Writers
commit bytes and position together. Readers advance only on `Done`.
`NeedMore` and `Invalid` preserve both position and the previous output value.
To retry with additional input, create a reader covering the enlarged buffer
and seek to the unfinished prefix.

```csharp
using Standard::Binary;

byte[10] buffer;
BinaryBufferWriter writer = new(buffer, 10);
bool written = writer.TryWriteVarUInt32(300u);
BinaryBufferReader reader = new(buffer, writer.Position);
uint value = 0u;
VarInt::Status status = reader.ReadVarUInt32(ref value); // Done; value is 300.
```

Signed helpers apply ZigZag before the same encoding:

```csharp
using Standard;
using Standard::Binary;

int main()
{
    byte[10] bytes;
    BinaryBufferWriter writer = new(bytes, 10);
    if (!writer.TryWriteZigZagInt64(-300l)) return 1;
    BinaryBufferReader reader = new(bytes, writer.Position);
    long value = 0l;
    if (reader.ReadZigZagInt64(ref value) != VarInt::Status::Done) return 2;
    if (value != -300l || reader.Remaining != 0) return 3;
    return 0;
}
```

## Example: header backfill and parser checkpoints

```csharp
using Standard;
using Standard::Binary;

int main()
{
    byte[8] bytes;
    BinaryBufferWriter writer = new(bytes, 8);
    if (!writer.TrySkip(2)) return 1;
    if (!writer.TryWriteByte((byte)10) || !writer.TryWriteByte((byte)20)) return 2;
    nuint end = writer.Position;
    if (!writer.TrySeek(0) || !writer.TryWriteUInt16BigEndian((ushort)(end - 2))) return 3;
    if (!writer.TrySeek(end) || writer.Position != 4) return 4;
    BinaryBufferReader reader = new(bytes, end);
    ushort length = 0;
    if (!reader.TryReadUInt16BigEndian(ref length) || length != 2) return 5;
    nuint checkpoint = reader.Position;
    if (!reader.TrySkip((nuint)length) || !reader.TrySeek(checkpoint)) return 6;
    byte first = (byte)0;
    if (!reader.TryReadByte(ref first) || first != (byte)10) return 7;
    return 0;
}
```

## Peeking at a bounded payload

`TryPeekSlice(count, ref slice)` returns a bounded reader without advancing the
parent. A successful child starts at position zero. An oversized request returns
false and resets the child to empty. Parent movement afterward leaves the child's
captured region intact.

```csharp
BinaryBufferReader header = new();
if (reader.TryPeekSlice(8, ref header))
{
    uint tag = 0u;
    uint length = 0u;
    header.TryReadUInt32LittleEndian(ref tag);
    header.TryReadUInt32LittleEndian(ref length);
    // Validate tag and length, then explicitly reader.TrySkip(8) when accepted.
}
```

## Shared counted-reader consumers

BinaryBufferReader implements `IByteReader`, so FileStream, TcpClient, and memory cursors
can use the same [stream helpers](BYTE_READERS.md). This advances the particular
reader passed by pointer.

Its `Read(output, count)` returns up to the remaining bytes and zero at EOF.
Output must be separate from the entire input region. Use `TryReadBytes` when
overlap support and an all-or-nothing result are needed.

Cursor varint methods preserve state on incomplete input. The
[stream varint methods](STREAM_VARINT.md) instead consume a prefix and throw
on truncation. Choose the form suited to the parser.

Sources: `core/Binary/BinaryBufferReader.gf`, `BinaryBufferWriter.gf`,
`BinaryPrimitives.gf`, and `binary_buffer_var_int.gf` for varint extensions.
