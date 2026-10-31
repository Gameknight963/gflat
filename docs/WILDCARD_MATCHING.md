# Counted byte wildcard matching

`Standard::Text::MatchesWildcard(text, length, pattern, count)` matches the complete
counted input, using ordinal, case-sensitive bytes. It returns a Boolean and is
`nothrow`. The core source is `core/Text/wildcard_matching.gf`, with no other
standard-library source dependency.

| Pattern byte | Meaning |
| --- | --- |
| `*` | Any sequence of bytes, including empty. |
| `?` | Exactly one byte. |
| `\` followed by a byte | That next byte literally, including `*`, `?`, or `\`. |
| Any other byte | The same byte literally. |

A trailing backslash is invalid and returns false. Repeated unescaped stars are
equivalent to one star. An empty pattern matches only empty input. Empty input
also matches a pattern containing only unescaped stars. Matching covers the whole
input, so pattern `abc` does not match input `xabcx`.

Counted NULs, newlines, high bytes, path separators and leading dots are ordinary
bytes. `?` matches one byte of a multibyte UTF-8 encoding, not a Unicode scalar.
There are no character classes, bracket expressions, globstar, path enumeration,
locale rules, case folding or UTF-8 validation. For example, `*.gf` is a simple
name filter. `*` can also consume slashes when the supplied input contains them.
Backslashes in input are ordinary bytes, while literal backslashes in a pattern
must be escaped by another backslash.

Both buffers must remain readable and stable for their counted extents throughout
the call. They may overlap or be identical. A zero length does not read that
buffer. No pointer is retained, no input is modified, and no storage is allocated.
The iterative matcher uses constant auxiliary storage and no recursion. Retrying
the suffix after a star can take O(length * count) work in the worst case. There
is no linear-time guarantee for adversarial patterns.

```csharp
using Standard::Text;
int main()
{
    if (!MatchesWildcard("report-01.csv", 13, "report-??.csv", 13)) return 1;
    if (!MatchesWildcard("a\0b", 3, "a?b", 3)) return 2;
    return 0;
}
```

`core/Text/cstring_wildcard.gf` adds `MatchesWildcard(text, pattern)` for two
readable, zero-terminated inputs, using `CString::Length` from `Text/CString.gf`.
These lengths stop at the first NUL. The core counted overload preserves all
counted bytes and needs no terminator.

`core/Text/text_wildcard_span.gf` adds three `Span<readonly(char)>` extensions:
`text.MatchesWildcard(patternSpan)`, `text.MatchesWildcard(pattern, count)`, and
`text.MatchesWildcard(cStringPattern)`. Include the usual Span dependencies and
`CString.gf` along with the core. Default empty spans are supported without
dereferencing null Data. The existing String `AsReadOnly()` extension and
mutable-span readonly views compose with these adapters. No owning result or String buffer copy is
created.
