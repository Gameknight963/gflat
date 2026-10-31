# Counted Unicode transcoding

`Standard::Text::UnicodeTranscoding` converts between UTF-8 bytes and UTF-16
`ushort` units.

| Method | Result units |
| --- | --- |
| `TryGetUtf16Length(input, bytes, ref units)` | Required UTF-16 units |
| `TryGetUtf8Length(input, units, ref bytes)` | Required UTF-8 bytes |
| `TryUtf8ToUtf16(input, bytes, output, capacity, ref written)` | UTF-16 units |
| `TryUtf16ToUtf8(input, units, output, capacity, ref written)` | UTF-8 bytes |

Capacity and written counts use the destination's units. Output is counted and
omits a terminator. U+0000 and U+FEFF are preserved.

```csharp
using Standard::Text;

ushort[32] wide;
nuint units = 0;
bool converted = UnicodeTranscoding::TryUtf8ToUtf16("hello", 5, wide, 32, ref units);
```

Malformed input, length overflow, or insufficient capacity returns false, sets
the result count to zero, and preserves output bytes. Conversion validates and
measures the entire input before writing.

Keep input, output, and count metadata separate. To convert serialized UTF-16LE
or UTF-16BE bytes, decode their byte order into `ushort` units first.

Source: `core/Text/UnicodeTranscoding.gf`. For owning results, use
[UnicodeText](UNICODE_TEXT.md).
