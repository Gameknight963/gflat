# Searching borrowed C strings

`CString::IndexOf(text, pattern)` and `LastIndexOf(text, pattern)` search for a
byte substring before the first NUL in each input. They return a byte offset or
`CString::NotFound` when a nonempty pattern is absent. Matching is exact and
case-sensitive, including bytes above ASCII.

```csharp
using Standard::Text;

int main()
{
    readonly(char)* text = "mode=fast;mode=safe";
    nuint first = CString::IndexOf(text, "mode=");
    nuint last = CString::LastIndexOf(text, "mode=");
    if (first != 0 || last != 10) return 1;
    if (!CString::Contains(text, "fast") || !CString::Contains(text, ';')) return 2;
    if (CString::Contains(text, "slow") || CString::Contains(text, '\0')) return 3;
    if (CString::IndexOf(text, "") != 0 || CString::LastIndexOf(text, "") != CString::Length(text)) return 4;
    return 0;
}
```

`Contains` accepts either a single `char` or a C-string pattern. Single-character
search excludes the terminator, so `Contains(text, '\0')` is false. An empty
pattern matches at the beginning for `IndexOf` and at the end for `LastIndexOf`.
`Contains(text, "")` is true, including for an empty text.

Forward empty-pattern searches return before reading text bytes. Other searches
read the inputs through their first NUL. Bytes after a NUL are not part of the
search. Use String's counted searches or ByteSearch when zero bytes belong to
the data being searched.

## Ownership, cost, and source selection

These functions borrow their inputs and never change them. Text and pattern
can overlap, including a pattern borrowed from a suffix of text. Returned offsets
refer to the original text's bytes. Keep the owner alive before deriving or using
a pointer from an offset.

All overloads are `nothrow` and allocate no storage. Substring searches reuse
[ByteSearch](BYTE_SEARCH.md), with O(text length + pattern length) worst-case work
and fixed scratch storage. They do not decode Unicode or apply culture rules.

Selective builds load `core/Text/CString.gf` and `core/Text/ByteSearch.gf`.
Freestanding builds can disable builtin-library substitution with `NoBuiltins`;
the search operations themselves require no native exception or IO bridge.
