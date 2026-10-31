# Owning Unicode conversions

`Standard::Text::UnicodeText` converts between UTF-8 Strings and UTF-16 vectors:

```csharp
using Standard::Text;

String UnicodeText::FromUtf16(readonly(ushort)* input, nuint units);
Vector<ushort> UnicodeText::ToUtf16(readonly(char)* input, nuint bytes);
```

| Operation | Result |
| --- | --- |
| `FromUtf16(input, units)` | UTF-8 String with its usual trailing NUL |
| `ToUtf16(input, bytes)` | `Vector<ushort>` of counted UTF-16 units |

The UTF-16 vector omits a terminator. For a native API requiring one, append
`(ushort)0` before borrowing its data. Embedded NUL and U+FEFF remain part of
the counted contents in either direction.

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

ushort[3] source = [(ushort)'A', (ushort)0xd83d, (ushort)0xde00];
String utf8 = UnicodeText::FromUtf16(source, 3);
Vector<ushort> utf16 = UnicodeText::ToUtf16(utf8.Data, utf8.Length);
```

Invalid encoding or size overflow throws `Exception`. Allocation failure throws
`BadAllocException`. Validation finishes before allocating the result.

UTF-16 inputs are host `ushort` values. Serialized LE/BE bytes need an explicit
byte-order decoder. For caller-buffer conversions, use
[UnicodeTranscoding](UNICODE_TRANSCODING.md).

Source: `core/Text/UnicodeText.gf`
