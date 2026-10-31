# ASCII byte utilities

`Standard::Text::Ascii` classifies, converts, and compares bytes using ASCII rules.

| Classification | Accepted bytes |
| --- | --- |
| `IsAscii` | 0–127 |
| `IsDigit` | `0`–`9` |
| `IsLower`, `IsUpper`, `IsLetter` | `a`–`z`, `A`–`Z`, or either |
| `IsLetterOrDigit` | ASCII letters and decimal digits |
| `IsHexDigit` | `0`–`9`, `a`–`f`, `A`–`F` |
| `IsWhiteSpace` | Space, tab, LF, vertical tab, form feed, CR |
| `IsControl` | 0–31 and 127 |
| `IsPrintable` | 32–126, including space |

Classification returns false for bytes above 127. `TryHexValue(input, result)`
writes a hexadecimal digit's value from 0 through 15. Invalid input returns false
and writes zero.

`ToLower` and `ToUpper` accept either one `char` or a buffer and byte count.
The buffer overload changes ASCII letters in place:

```csharp
using Standard::Text;

char[4] bytes = ['A', '\0', 'z', (char)255];
Ascii::ToLower(bytes, 4); // ['a', '\0', 'z', 255]
```

## Counted case-insensitive comparison

| Parameter | Meaning |
| --- | --- |
| `left`, `right` | Text to compare |
| `leftLength`, `rightLength` | Respective byte counts |

```csharp
using Standard::Text;

bool command = Ascii::EqualsIgnoreCase(input, inputLength, "help", 4);
int order = Ascii::CompareIgnoreCase(left, leftLength, right, rightLength);
```

`EqualsIgnoreCase` compares after folding ASCII letters to uppercase.
`CompareIgnoreCase` uses the same folding and returns -1, 0, or 1 in unsigned-byte
lexicographic order. A shorter equal prefix sorts first. Uppercase folding means
`z` sorts before `[`.

Nonletters match exactly, including embedded NULs and bytes above 127.
Differently cased accented letters encoded in UTF-8 therefore compare unequal.

## Null-terminated comparison

Two-argument overloads compare C strings:

```csharp
using Standard::Text;

bool command = Ascii::EqualsIgnoreCase(input, "help");
int order = Ascii::CompareIgnoreCase(left, right);
```

They use the same comparison rules and stop at the first NUL. Use the counted
forms when a NUL is part of the data.

## Case-insensitive hashing

`HashIgnoreCase(data, count)` applies the same uppercase folding. Pair it with
`EqualsIgnoreCase` for case-insensitive keys:

```cpp
using Standard;
using Standard::Text;
using Standard::Collections;

struct HeaderName : IHashable, IEquatable<HeaderName>
{
    private readonly(char)* text;
    private nuint length;
    public HeaderName(readonly(char)* bytes, nuint count)
    {
        text = bytes;
        length = count;
    }
    public immutable nuint GetHashCode() nothrow
        => Ascii::HashIgnoreCase(text, length);
    public immutable bool Equals(immutable(HeaderName)* other) nothrow
        => Ascii::EqualsIgnoreCase(text, length, other.text, other.length);
}

int main()
{
    HashMap<HeaderName, int> fields = new();
    fields.Add(new HeaderName("Content-Type", 12), 7);
    HeaderName query = new("content-type", 12);
    int*? found = fields.Find(&query);
    if (found == null) return 1;
    if (*found! != 7) return 2;
    return 0;
}
```

The example stores pointers to static literals. When keys point into other
storage, keep those bytes alive and unchanged for the map's lifetime.

The hash uses 32-bit FNV-1a widened to `nuint`. Use it for collection lookup.
Its value can change between library versions, and deliberately chosen keys can
cause collisions.

## Counted case-insensitive substring search

| Operation | Result when found | Empty pattern |
| --- | --- | --- |
| `IndexOfIgnoreCase(data, length, pattern, count)` | First matching byte offset | 0 |
| `LastIndexOfIgnoreCase(data, length, pattern, count)` | Last matching byte offset | `length` |

Both return `length` when absent or when the pattern is longer than the input.
The reverse search's empty-pattern result shares that sentinel.

```csharp
nuint first = Ascii::IndexOfIgnoreCase(text, textLength, "error", 5);
nuint last = Ascii::LastIndexOfIgnoreCase(text, textLength, "error", 5);
```

Matching uses the comparison rules above. The searches take O(`length * count`)
time in the worst case and constant working storage.

Source: `core/Text/ascii.gf`
