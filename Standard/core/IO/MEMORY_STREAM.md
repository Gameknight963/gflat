# Memory streams

`Standard::IO::MemoryStream` owns counted bytes and a cursor. It implements
`IByteReader` and `ITextWriter`, so reader, formatting, and encoding helpers can
use it as a source or destination.

```csharp
using Standard::IO;

int main()
{
    MemoryStream stream = new();
    ITextWriter* output = &stream;
    output.Write("A\0B", 3);
    stream.Position = 0l;
    char[3] bytes;
    IByteReader* input = &stream;
    if (input.Read(bytes, 3) != 3 || bytes[1] != '\0') return 1;
    return 0;
}
```

## Contents and position

`Length` and `Position` use signed 64-bit `long` values. `Capacity` uses `nuint`.
The default constructor starts empty. `MemoryStream(capacity)` reserves at least
the requested byte capacity.

- `Read(data, count)` reads up to the remaining bytes and advances Position
- `ReadByte()` returns 0–255 and advances, or returns -1 at EOF
- `PeekByte()` returns the same values while retaining Position
- `Write(data, count)` overwrites at Position and extends Length when necessary
- `WriteByte(value)` writes one char byte
- `Flush()` completes immediately because writes update the owned buffer directly

A zero-count read or write leaves state unchanged. Read destinations must be
separate from the stream and its backing storage, following `IByteReader`'s
contract. A write source can borrow the stream's current contents, including an
overlapping destination. Self-source writes remain valid when growth relocates
the buffer.

`Seek(offset, origin)` uses `SeekOrigin::Begin`, `Current`, or `End`, and returns
the new position. Seeking beyond Length allocates nothing. A later write fills
the gap with zeros. Negative positions and overflowing seeks throw `IOException`.

## Resizing and capacity

`SetLength(length)` truncates or zero-extends the contents. Position remains
unchanged, including positions beyond the new end. Length must fit both a
nonnegative `long` and the native allocation size.

`Reserve(capacity)` ensures at least the requested capacity while retaining
Length, Position, and contents. `Clear()` resets Length and Position to zero
and retains capacity.

Allocation failures throw `BadAllocException`. Failed writes, resizes, and
reservations preserve the existing contents, Length, and Position. Invalid
ranges throw `IOException` before mutation.

## Ownership and views

Copying a stream creates independent contents and preserves its cursor. Moving
transfers its buffer. Destruction releases the owned allocation.

`AsSpan()` borrows mutable contents and `AsReadOnly()` borrows readonly contents.
Views cover Length bytes and retain their captured ranges. Keep the buffer's
owner alive and reacquire views after capacity growth. Shrinking or clearing
invalidates removed ranges.

## Source selection

Selective builds include `IO/MemoryStream.gf`, `IO/SeekOrigin.gf`, `IO/IOException.gf`,
`IO/IByteReader.gf`, `Collections/Vector.gf`, `Collections/vector_resize.gf`, and
the Vector/Span collection and query contracts. Ordinary Standard projects include them.
