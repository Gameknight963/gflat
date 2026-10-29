# Borrowed binary buffer cursors

`std::Binary::BinaryBufferReader` and `std::Binary::BinaryBufferWriter` track a position within
caller-owned bytes. They do not own storage, allocate, or directly call libc. All
constructors, properties, and methods are `nothrow`. Include their respective
source files, `core/Binary/BinaryPrimitives.gf`, and `core/Memory/bytes.gf`.

```csharp
using std; using std::Binary;
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

Construct a reader with `readonly(byte)*` and a readable byte count; construct
a writer with `byte*` and writable capacity. A parameterless constructor creates
an empty cursor. `Length` (reader) and `Capacity` (writer) report the captured
extent. Both have `Position` and `Remaining` getters. `Reset()` sets position to
zero without modifying storage. `TrySkip(count)` advances only if enough bytes
remain; a writer skip does not initialize the skipped bytes.

`TrySeek(position)` sets an absolute position within the cursor's captured region.
Positions from zero through `Length` or `Capacity`, including the end, succeed;
an out-of-range position returns false and leaves the old position unchanged.
It can move forward or backward without reading, writing, clearing or initializing
bytes, allocating storage or computing an address from `Data`. A default empty
cursor accepts only zero. Child positions are relative to the child's own region,
and seeking one cursor does not change a copy's or child's position.

This supports parser checkpoints and packet-header backfill without a destructive
`Reset()` followed by a potentially failing skip. A writer's `Position` is its
current cursor, not a high-water mark: save and restore the end position when
backfilling, and do not treat a forward seek as proof that bytes were initialized.
Backing-storage lifetime and validity rules still apply. `Reset` and `TrySkip`
keep their existing behavior.


`Data` exposes the start of the captured region, independently of the current
position: a reader returns `readonly(byte)*?`, and a mutable writer returns
`byte*?`. A default cursor has null data. This lets a bounded child feed another
counted API, such as UTF-8 decoding, without copying the payload or adding a Span
dependency. Direct pointer use requires the caller to respect `Length` or
`Capacity`; it does not advance the cursor or perform bounds checks.

`TryPeekByte(ref byte value)` observes the next byte without advancing. It returns
true with that byte, or false with output zero for an empty or exhausted reader.
Its immutable, `nothrow` receiver permits mutable, readonly and immutable views.
Neither result changes position, and repeated peeks observe the same position
(with any caller edits to backing bytes still visible). It allocates nothing and
retains no output reference. The initialized writable output must not overlap the
reader's fields. A default empty reader accesses no input storage.

`TryReadByte` / `TryWriteByte` handle one byte. Fixed-width methods use
`TryRead` / `TryWrite` followed by `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`,
`UInt64`, `ExtraLong`, `Float`, or `Double`, and `LittleEndian` or `BigEndian`, matching
`BinaryPrimitives`. Signed operations preserve two's-complement bits. Floating
operations preserve IEEE bits, including signed zero and NaN payloads; their
widths are four and eight bytes respectively. `ExtraLong` reads or writes all
16 bytes of a signed 128-bit `extralong`. Addresses need not be aligned
for the scalar's width; reads and writes operate one byte at a time.

On success, a fixed-width operation advances by its byte width. A failure leaves
position unchanged; reads set their output to zero (positive zero for floating
values), and writes leave bytes
untouched. Operations check remaining capacity before adding an offset, avoiding
pointer-sized position overflow.

## Bounded payloads

`TryReadBytes(byte* output, nuint count)` copies the next `count` bytes into
caller-provided storage. `TryWriteBytes(readonly(byte)* input, nuint count)`
copies from caller-provided storage into the next `count` bytes. Both advance
only after a successful copy. Insufficient remaining bytes leave the position
and destination bytes unchanged. Source and destination may overlap, including
identical regions: copying uses `Memory::Move`. An overlapping read may change
bytes that a later read will observe.

Zero counts succeed without accessing either pointer, including for a default
empty cursor. Pointer arguments still have non-null types. For positive counts,
the caller supplies live readable/writable regions of at least that size;
neither region may overlap the cursor object's fields. The methods check only
the cursor's remaining extent, not the allocation behind a caller pointer.

The [raw-memory linking notes](MEMORY.md#native-linking) also apply: an optimizer
may generate libc byte-operation calls. Freestanding callers can select
`NoBuiltins` and must inspect their target's final linkage.

`TryReadSlice(count, ref BinaryBufferReader slice)` consumes the next `count`
bytes and returns a reader bounded to just that region. The writer equivalent,
`TryWriteSlice(count, ref BinaryBufferWriter slice)`, reserves that region and
returns a bounded writer. Reserving does not initialize the bytes. The parent
advances immediately; the child's position starts at zero and moves independently.
Both operations accept zero length and return an empty child. On failure the
parent position is unchanged and the output child is reset to empty.

Keep the backing bytes alive for every cursor and child using them. Copies have
independent positions over the same storage. Reader results see changes made
through a writer or caller pointer. A slice output must not alias its parent
cursor. Store cursor objects separately from their backing byte buffers, and
keep output variables separate from the parent cursor's fields. Scalar read
outputs may overlap input bytes under `BinaryPrimitives` rules,
but a result store can overwrite bytes needed by a future read. No method
validates a raw pointer's actual allocation or protection.

## Optional variable-length integers

`Binary/binary_buffer_var_int.gf` integrates the standalone [VarInt codec](VARINT.md)
without adding that dependency to the cursor source files. Include it and
`Binary/VarInt.gf` when selecting sources individually.

```csharp
using std::Binary;
bool TryWriteVarUInt32(this ref BinaryBufferWriter writer, uint value) nothrow;
bool TryWriteVarUInt64(this ref BinaryBufferWriter writer, ulong value) nothrow;
VarInt::Status ReadVarUInt32(this ref BinaryBufferReader reader, ref uint value) nothrow;
VarInt::Status ReadVarUInt64(this ref BinaryBufferReader reader, ref ulong value) nothrow;
bool TryWriteZigZagInt32(this ref BinaryBufferWriter writer, int value) nothrow;
bool TryWriteZigZagInt64(this ref BinaryBufferWriter writer, long value) nothrow;
VarInt::Status ReadZigZagInt32(this ref BinaryBufferReader reader, ref int value) nothrow;
VarInt::Status ReadZigZagInt64(this ref BinaryBufferReader reader, ref long value) nothrow;
```

These use canonical unsigned base-128 encoding. Writers advance only after a
complete write; short buffers leave every byte and the position unchanged.
Readers advance only on `Done`; `NeedMore` and `Invalid` preserve both the
position and output value. To retry incomplete input, construct a reader covering
the enlarged buffer and position it at the same unfinished prefix. An existing
reader's captured length does not grow when backing bytes are appended.
Empty or exhausted readers return `NeedMore`, and writers
return false, without accessing or calculating an address from their data pointer.
All eight methods allocate nothing and are `nothrow`.

The signed helpers apply ZigZag before the same canonical unsigned base-128
encoding: `0` becomes `0`, `-1` becomes `1`, and `1` becomes `2`. Both signed
minima and maxima are supported. This is the ZigZag representation; negative
values do not use a signed two's-complement 7-bit encoding. Signed reads preserve
their previous output and position on `NeedMore` or `Invalid`, and signed writes
leave every byte and position unchanged when capacity is insufficient.

```csharp
using std::Binary;
byte[10] buffer;
BinaryBufferWriter writer = new(buffer, 10);
bool written = writer.TryWriteVarUInt32(300u);
BinaryBufferReader reader = new(buffer, writer.Position);
uint value = 0u;
VarInt::Status status = reader.ReadVarUInt32(ref value); // Done; value is 300.
```

A reader decodes into local metadata before committing the caller's result.
The output reference must be separate from the cursor object's fields, but may
overlap suitably aligned writable input storage. A successful result store can
therefore change bytes observed by later reads. The codec's own temporary
metadata remains separate from the buffer. Normal borrowed-storage lifetime
and validity requirements still apply. The signed helpers decode through local
unsigned metadata before committing the signed result, with the same output
alias restrictions as the unsigned helpers.

```csharp
using std; using std::Binary;
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
using std; using std::Binary;

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
