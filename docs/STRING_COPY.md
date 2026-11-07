# Copying string bytes into a buffer

`String.CopyTo` copies counted bytes into a writable `Span<char>`, either for the
whole string or for a selected range. `TryCopyTo` returns false without writing
when the destination is too short for the whole string.

```csharp
using Standard::Memory;
using Standard::Text;

int main()
{
    String text = s"hello";
    char[7] buffer = ['.', '.', '.', '.', '.', '.', '.'];
    Span<char> output = new(&buffer[0], 7);
    text.CopyTo(output);
    text.CopyTo(1, output, 5, 2);
    if (buffer[0] != 'h' || buffer[4] != 'o' || buffer[5] != 'e' || buffer[6] != 'l') return 1;
    if (text.TryCopyTo(output.Slice(4))) return 2;
    return 0;
}
```

## Bounds and byte semantics

- `CopyTo(destination)` copies `Length` bytes to the start of the span
- `CopyTo(sourceIndex, destination, destinationIndex, count)` copies the selected byte range
- `TryCopyTo(destination)` is `nothrow` and returns false for insufficient space

The throwing forms validate the complete range before writing. Indices may equal
their respective lengths for an empty copy. Destination bytes outside the copied
range remain unchanged. Empty strings copy successfully into empty spans.

Indices and counts are bytes, not Unicode scalar positions or UTF-16 code units.
Embedded NUL bytes are copied. The string's terminating NUL is excluded, and no
terminator is appended to the destination. To construct a C string, provide and
write the extra terminator yourself or use [CString copying](CSTRING.md).

## Storage and aliasing

The source can be immutable. Copying allocates no storage, changes no string
length or capacity, and supports overlapping buffers in either direction.
Writing through another mutable alias can change the source bytes, so an
overlapping copy does not preserve the source contents. If the destination
borrows string storage, keep that owner's length and terminating NUL valid.

For explicit source lists, include `Text/string_copy.gf`, `Memory/Span.gf`, and
`Memory/bytes.gf` along with the string's dependencies. Freestanding forms take
an `immutable(String)*` before the same method arguments, such as
`CopyTo(source, destination)`.
