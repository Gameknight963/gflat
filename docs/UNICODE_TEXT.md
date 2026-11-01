# Owning Unicode text operations

`Standard::Text::UnicodeText` provides owning UTF-8/UTF-16 conversions,
Unicode whitespace predicates, and trimming.

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

## Whitespace checks

| Predicate | Input |
| --- | --- |
| `IsWhiteSpace(input, bytes)` | Counted UTF-8. Empty input returns true |
| `IsNullOrWhiteSpace(input)` | Nullable C string. Null or empty input returns true |

Both return false at a non-whitespace scalar or malformed UTF-8 sequence.
Classification uses [`Rune::IsWhiteSpace`](RUNE.md).

The C-string form stops at the first NUL and ignores following bytes. A NUL
inside counted input returns false.

```csharp
using Standard::Text;

int main()
{
    char[3] text = [' ', '\0', (char)255];
    if (!UnicodeText::IsNullOrWhiteSpace(text)) return 1;
    if (UnicodeText::IsWhiteSpace(text, 3)) return 2;
    if (!UnicodeText::IsNullOrWhiteSpace(null)) return 3;
    return 0;
}
```

## Trimming Unicode whitespace

`UnicodeText::Trim`, `TrimStart`, and `TrimEnd` return independent Strings after
removing Unicode whitespace from the selected ends. Each accepts either counted
UTF-8 bytes or a C string. The counted form preserves embedded NUL. U+FEFF (BOM)
is retained.

```csharp
using Standard::Text;

int main()
{
    // UTF-8 bytes for U+2003, x, U+00A0.
    char[6] source = [(char)0xe2, (char)0x80, (char)0x83, 'x', (char)0xc2, (char)0xa0];
    String result = UnicodeText::Trim(source, 6);
    if (!result.Equals("x")) return 1;
    return 0;
}
```

The complete UTF-8 input is validated before allocating a result, including its
untrimmed middle. Invalid encoding throws `Exception`. Classification uses
[`Rune::IsWhiteSpace`](RUNE.md). The in-place `String.Trim` methods keep their
byte-oriented behavior.

Source: `core/Text/UnicodeText.gf`
