# Unicode scalar values

`Standard::Text::Rune` stores one validated Unicode scalar. Construction accepts
U+0000 through U+10FFFF except U+D800 through U+DFFF and throws `Exception` for
invalid values. Default construction is U+0000.

| Property | Meaning |
| --- | --- |
| `Value` | Numeric `uint` scalar |
| `IsAscii` | True for U+0000–U+007F |
| `Utf8Length` | 1–4 encoded bytes |
| `Utf16Length` | 1–2 encoded units |

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

`Format` and interpolation write the scalar's UTF-8 encoding. `ToString()`
returns that encoding in an owning String. U+0000 produces a String of length
one, whose contents appear empty to a C-string consumer.

Equality and ordering compare scalar numbers. `Utf8Scalars` and `Utf16Scalars`
yield `uint`, which can be converted with `new Rune(scalar)`.

## Whitespace

`Rune::IsWhiteSpace(value)` tests the Unicode
[White_Space property](https://www.unicode.org/Public/18.0.0/ucd/PropList.txt).
It includes ASCII whitespace, U+0085, and the Unicode space, line, and paragraph
separators. U+FEFF, U+180E, and U+200B return false.

```csharp
using Standard::Text;

int main()
{
    Rune space = new(0xa0u);
    Rune zeroWidthSpace = new(0x200bu);
    if (!Rune::IsWhiteSpace(space) || Rune::IsWhiteSpace(zeroWidthSpace)) return 1;
    return 0;
}
```

Source: `core/Text/Rune.gf`
