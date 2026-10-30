# Counted Unicode transcoding

`Standard::Text::UnicodeTranscoding` converts whole buffers between strict UTF-8 and UTF-16.
Include `core/Text/UnicodeTranscoding.gf`, `core/Text/utf8.gf`, and `core/Text/utf16.gf`. It uses
no String, GC, allocation, libc, replacement characters, or exception support.
All four methods are `nothrow`.

| Method | Result |
| --- | --- |
| `TryGetUtf16Length(input, bytes, ref units)` | Required UTF-16 code units for counted UTF-8 input. |
| `TryGetUtf8Length(input, units, ref bytes)` | Required UTF-8 bytes for counted UTF-16 input. |
| `TryUtf8ToUtf16(input, bytes, output, capacity, ref written)` | Write UTF-16 units; capacity and written count units. |
| `TryUtf16ToUtf8(input, units, output, capacity, ref written)` | Write UTF-8 bytes; capacity and written count bytes. |

UTF-8 storage is `char*`; UTF-16 storage is `ushort*`, containing host-order code
unit values. This does not serialize UTF-16LE or UTF-16BE bytes. Embedded nulls
are ordinary scalar values. Nothing appends a terminator, strips a BOM, or
normalizes text.

```csharp
using Standard::Text;
ushort[32] wide;
nuint units = 0;
bool converted = UnicodeTranscoding::TryUtf8ToUtf16("hello", 5, wide, 32, ref units);
```

Lengths return false and zero their result for malformed input or an
unrepresentable output length. Conversion validates the entire input and output
capacity before writing: failure writes zero to `written` and leaves the output
buffer untouched. Successful conversion writes exactly `written` elements;
storage after that remains untouched. Conversion makes two passes over input.

Keep source, destination, and result metadata disjoint and valid for their
declared extents. Do not change input during a call. Zero-length operations do
not dereference input or output. These APIs do not check raw pointers'
allocations, protection, or alignment.
