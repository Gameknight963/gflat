# Unicode scalar values

`Rune` stores one validated Unicode scalar in a `uint`. Its default value is
U+0000. `new Rune(value)` accepts U+0000 through U+10FFFF except the surrogate
range U+D800 through U+DFFF; invalid input throws `Exception` with the message
`Invalid Unicode scalar value`.

`Value` returns the numeric scalar. `IsAscii` includes U+0000 through U+007F.
`Utf8Length` counts encoded bytes (1–4), while `Utf16Length` counts code units
(1–2). All four getters are `nothrow`.

```csharp
using Standard::Text;
using Standard;
int main()
{
    Rune face = new(0x1f600u);
    if (face.IsAscii || face.Utf8Length != 4 || face.Utf16Length != 2) return 1;
    String text = s$"{face}";
    if (text.Length != 4) return 2;
    Rune zero = new();
    String nul = zero.ToString();
    if (nul.Length != 1 || nul.Data[0] != '\0') return 3;
    return 0;
}
```

`Format` writes the scalar's counted UTF-8 encoding to a borrowed `ITextWriter`.
It uses four stack bytes and propagates writer failures. Interpolation emits
that encoding rather than a decimal code-point number. `ToString()` returns an
independent owning UTF-8 `String`, retaining U+0000 as a byte with length one.
Counted output preserves this byte; a C-string consumer sees a terminator.

Equality compares scalar values, ordering follows numeric scalar order, and
equal values have equal hashes. This order is unrelated to linguistic collation.
`Rune` owns no allocation; string conversion and interpolation use their normal
owning `String` allocation behavior.

Existing `Utf8Scalars` and `Utf16Scalars` continue yielding `uint`. Construct a
`Rune` explicitly for a validated-value API or use an ordinary query projection:
`.Select((uint scalar) => new Rune(scalar))`. Decoders and encoders retain their
current APIs, and `Utf8::TryEncode(rune.Value, ...)` remains available.

A scalar is not necessarily a displayed character or grapheme cluster.
`Rune` adds no Unicode category tables, case conversion, normalization, display
width, or cultural rules. Selective source users include `Text/Rune.gf`, `Text/utf8.gf`,
`Text/utf16.gf`, equality/order/hash and text contracts, `Hashing/primitive_hashing.gf`, and `String`
dependencies for its owning conversion contract.
