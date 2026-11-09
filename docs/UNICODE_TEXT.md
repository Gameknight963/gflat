# Owning Unicode text operations

`Standard::Text::UnicodeText` provides owning UTF-8/UTF-16 conversions,
Unicode whitespace predicates, and trimming.

```csharp
using Standard::Collections;
using Standard::Text;

String UnicodeText::FromUtf16(readonly(ushort)* input, nuint units);
String UnicodeText::FromUtf16(readonly(ushort)* input);
Vector<ushort> UnicodeText::ToUtf16(readonly(char)* input, nuint bytes);
Vector<ushort> UnicodeText::ToUtf16CString(readonly(char)* input, nuint bytes);
```

| Operation | Result |
| --- | --- |
| `FromUtf16(input, units)` | UTF-8 String with its usual trailing NUL |
| `FromUtf16(input)` | UTF-8 String read through the first zero UTF-16 unit |
| `ToUtf16(input, bytes)` | `Vector<ushort>` of counted UTF-16 units |
| `ToUtf16CString(input, bytes)` | `Vector<ushort>` including a final zero unit |

`ToUtf16` omits a terminator. `ToUtf16CString` includes the final zero in `Count`
and allocates once, including for empty input. Embedded NUL and U+FEFF remain
part of the counted contents in all conversions.

The one-argument `FromUtf16` overload requires a null-terminated input and excludes
the terminator from the result. Units after the first zero are ignored. Use the
counted overload to preserve embedded zeros.

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

## Native UTF-16 strings

Keep the vector alive and its storage stable while native code retains `AsSpan().Data`.
Embedded zeros remain in the buffer, so use a counted native API when the whole
input must be preserved. Pass `Count - 1` when that API excludes the terminator.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    // UTF-8 for A, U+00E9, U+1F642.
    char[7] input = ['A', (char)0xc3, (char)0xa9, (char)0xf0, (char)0x9f, (char)0x99, (char)0x82];
    Vector<ushort> text = UnicodeText::ToUtf16CString(input, 7);
    if (text.Count != 5 || text[4] != 0) return 1;
    String roundTrip = UnicodeText::FromUtf16(text.AsSpan().Data!);
    if (roundTrip.Length != 7) return 2;
    for (nuint index = 0; index < 7; index++)
        if (roundTrip.Data[index] != input[index]) return 3;
    return 0;
}
```

Source: `core/Text/UnicodeText.gf`
