# Integer parsing

`Standard::Text` provides decimal parsers named for their result type: `Byte`,
`SByte`, `Short`, `UShort`, `Int`, `UInt`, `Long`, `ULong`, `ExtraLong`, `NInt`,
and `NUInt`. Each has counted and C-string forms:

```cpp
using Standard::Text;

bool TryParseLong(readonly(char)* text, long* result) nothrow;
bool TryParseLong(readonly(char)* text, nuint count, long* result) nothrow;
long ParseLong(readonly(char)* text);
long ParseLong(readonly(char)* text, nuint count);
```

| Input | Rule |
| --- | --- |
| Digits | Decimal `0`–`9`, with optional leading `+` |
| Negative values | Leading `-` for signed results. Unsigned parsers reject `-0` too. |
| Leading zeroes | Accepted |
| Whitespace, separators, radix prefixes, trailing text | Rejected |
| Empty input or a sign alone | Rejected |
| Value outside the result type's range | Rejected |

`TryParse...` returns false and writes zero on failure. `Parse...` throws
`Exception`. `NInt` and `NUInt` use target pointer limits. `ExtraLong` uses the
signed 128-bit range, from `-170141183460469231731687303715884105728` through
`170141183460469231731687303715884105727`.

C-string forms stop at the first NUL. Counted forms validate every selected
byte, including embedded NUL, which is invalid decimal text. Output may overlap
aligned writable input storage. The parser finishes reading before storing the
result, including the zero written on failure.

String extensions parse the String's complete contents:

```cpp
using Standard::Text;

String text = s"18446744073709551615";
ulong number = text.ParseULong();
ulong attempted = 0ul;
bool valid = text.TryParseULong(&attempted);
```

For hexadecimal input, use the [unsigned hexadecimal parsers](HEX_INTEGER_PARSING.md).

## Counted span extensions

`Span<readonly(char)>` offers the same `TryParse...` and `Parse...` methods:

```csharp
using Standard::Text;
using Standard::Memory;

char[4] buffer = ['x', '1', '2', 'x'];
Span<readonly(char)> text = new(&buffer[1], 2);
long value = text.ParseLong(); // 12; neither surrounding byte is read.
long attempted = 0l;
bool valid = text.TryParseLong(&attempted);
```

An empty span fails. The Try form writes zero. These methods also accept an
immutable span header.

Sources in `core/Text`:

- `parsing.gf`: `Int` forms
- `integer_parsing.gf`: other widths
- `string_parsing.gf`, `string_integer_parsing.gf`: String extensions
- `span_integer_parsing.gf`, `span_integer_parsing_throwing.gf`: span extensions

The pointer parser files contain throwing methods as well as `nothrow` Try
methods, so selecting those files also requires the exception runtime.
