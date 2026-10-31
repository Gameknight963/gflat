# Counted byte wildcard matching

`Standard::Text::MatchesWildcard(text, length, pattern, count)` tests a complete
byte string against a case-sensitive pattern.

| Pattern | Meaning |
| --- | --- |
| `*` | Any byte sequence, including empty |
| `?` | One byte |
| `\` followed by a byte | The next byte literally |
| Other bytes | The same byte literally |

```csharp
using Standard::Text;

int main()
{
    if (!MatchesWildcard("report-01.csv", 13, "report-??.csv", 13)) return 1;
    if (!MatchesWildcard("a\0b", 3, "a?b", 3)) return 2;
    return 0;
}
```

A trailing backslash is invalid. An empty pattern matches empty input. Stars can
match path separators and NUL bytes. `?` consumes one UTF-8 byte, which may be
only part of a Unicode scalar. The iterative matcher uses constant working
storage and O(`length * count`) worst-case time.

`MatchesWildcard(text, pattern)` accepts C strings. Character spans offer
`text.MatchesWildcard(patternSpan)`, a counted-pattern overload, and a C-string
pattern overload. Input and pattern may overlap.

Sources in `core/Text`: `wildcard_matching.gf`, `cstring_wildcard.gf`, and
`text_wildcard_span.gf`.
