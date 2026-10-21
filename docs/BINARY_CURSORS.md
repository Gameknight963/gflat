# Borrowed binary buffer cursors

`std::BinaryBufferReader` and `std::BinaryBufferWriter` track a position within
caller-owned bytes. They do not own storage, allocate, or depend on libc. All
constructors, properties, and methods are `nothrow`. Include their respective
source files and `core/BinaryPrimitives.gf`.

```csharp
using std;
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

`Data` exposes the start of the captured region, independently of the current
position: a reader returns `readonly(byte)*?`, and a mutable writer returns
`byte*?`. A default cursor has null data. This lets a bounded child feed another
counted API, such as UTF-8 decoding, without copying the payload or adding a Span
dependency. Direct pointer use requires the caller to respect `Length` or
`Capacity`; it does not advance the cursor or perform bounds checks.

`TryReadByte` / `TryWriteByte` handle one byte. Fixed-width methods use
`TryRead` / `TryWrite` followed by `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`,
`UInt64`, `Float`, or `Double`, and `LittleEndian` or `BigEndian`, matching
`BinaryPrimitives`. Signed operations preserve two's-complement bits. Floating
operations preserve IEEE bits, including signed zero and NaN payloads; their
widths are four and eight bytes respectively. Addresses need not be aligned
for the scalar's width; reads and writes operate one byte at a time.

On success, a fixed-width operation advances by its byte width. A failure leaves
position unchanged; reads set their output to zero (positive zero for floating
values), and writes leave bytes
untouched. Operations check remaining capacity before adding an offset, avoiding
pointer-sized position overflow.

## Bounded payloads

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
