# ASCII case-insensitive text predicates

`Ascii::StartsWithIgnoreCase`, `EndsWithIgnoreCase`, and `ContainsIgnoreCase`
accept either two C strings or two pointer/count pairs. They fold ASCII letters
`a` through `z` to uppercase and compare all other bytes unchanged. This is not
Unicode case folding or culture-sensitive comparison.

```csharp
using Standard::Text;

int main()
{
    if (!Ascii::StartsWithIgnoreCase("Content-Type", "content-")) return 1;
    if (!Ascii::EndsWithIgnoreCase("REPORT.CSV", ".csv")) return 2;
    if (!Ascii::ContainsIgnoreCase("Header: Value", "VALUE")) return 3;
    char[3] binary = ['A', (char)0, 'b'];
    char[2] suffix = [(char)0, 'B'];
    if (!Ascii::EndsWithIgnoreCase(&binary[0], 3, &suffix[0], 2)) return 4;
    return 0;
}
```

Counted inputs include embedded null bytes. C strings stop at the first null.
Empty patterns always match. Counted empty patterns and oversized patterns do
not read either input pointer. For C strings, an empty pattern is checked before
reading the data pointer.

All operations are allocation-free and leave their inputs unchanged. Prefix and
suffix comparison take linear time in the pattern length, plus C-string length
scans for suffix comparison. Containment uses the existing substring search,
with worst-case time proportional to the product of the two lengths.
