# Explicit UUID text formats

`Uuid` supports explicit `N`, `D`, `B`, and `P` format codes. Codes are
case-insensitive; emitted hexadecimal letters are lowercase.

| Code | Representation |
| --- | --- |
| `N` | 32 hexadecimal digits |
| `D` | 8-4-4-4-12 hyphenated digits |
| `B` | Hyphenated digits inside braces |
| `P` | Hyphenated digits inside parentheses |

```csharp
using Standard;
using Standard::Text;

int main()
{
    Uuid id = Uuid::ParseExact("00112233445566778899aabbccddeeff", 'N');
    String braced = id.ToString('B');
    if (!braced.Equals("{00112233-4455-6677-8899-aabbccddeeff}")) return 1;
    Uuid roundTrip = Uuid::ParseExact(braced.Data, 'B');
    if (!roundTrip.Equals(&id)) return 2;
    return 0;
}
```

`TryParseExact(text, count, format, result)` accepts counted text, while the
C-string form omits `count`. Both are `nothrow` and reset the result to Nil
on failure. `ParseExact` has the same input forms and throws for a mismatch
or unsupported code. Parsing accepts either hexadecimal case, requires exact
separator and wrapper positions, and rejects surrounding whitespace and
embedded zeros. Format `X` is not supported.

`TryFormat(output, capacity, ref written, format)` writes 32, 36, or 38 bytes
without a terminator. Invalid codes and short buffers return false, set
`written` to zero, and leave output unchanged. The receiver is snapshotted
before writing; `written` must be separate from the receiver and output.
`Format(writer, format)` throws for an unsupported code before calling the
writer. Writer errors can leave partial output.

Load `Standard/core/Text/uuid_string.gf` for owning `ToString(format)` in
`Standard::Text`. Unsupported codes throw before allocation. General
`ToString()` remains available through ordinary formattable conversion.
The existing `Parse`, `TryParse`, and format-free formatting methods continue
to use only `D`. Byte conversion and UUID ordering are unchanged.
