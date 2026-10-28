# Split borrowed text once on a byte-sequence delimiter

Optional `core/Text/text_split_once_pattern_span.gf` adds three non-generic overloads on
`Span<readonly(char)>`, complementing the existing single-character API:

- `TrySplitOnce(separator, count, ref before, ref after)` takes counted bytes.
- `TrySplitOnce(separator, ref before, ref after)` takes a null-terminated pointer.
- `TrySplitOnce(separatorSpan, ref before, ref after)` takes the complete
  counted contents of a `Span<readonly(char)>`, including NUL bytes.

The first complete exact byte match wins. Matching is case-sensitive and has no
Unicode normalization, quoting, escaping, or word-boundary rules. The separator
is excluded from both results; later matches remain in the second result.
Search reuses `ByteSearch`, rather than introducing another pattern algorithm.

An empty separator matches at offset zero: `before` is an empty default span and
`after` is the complete receiver. This also succeeds for a default empty source
or separator span without accessing a null Data pointer. A separator longer than
the source fails without reading either byte buffer. A counted separator of
length zero is not dereferenced. The C-string convenience scans its separator to
find the terminator and therefore requires readable terminated storage.

Failure returns false and clears both output spans to their default empty state.
Both successful views are computed before output metadata is assigned. Either
output may reuse the caller's receiver variable or separator-span variable;
output references must be distinct from one another.

All results borrow the original text storage. Keep it alive and at the same
address while using either result. Changing source bytes is visible through
returned views. No separator pointer is retained, so its storage is needed only
during the call. Sources/separators may overlap each other; output metadata must
not overlap the byte storage being searched. Calls do not allocate, change input
bytes, construct Strings, or require disposal. Ordinary raw-pointer validity
requirements still apply for nonempty accepted buffers.

Include `Memory/Span.gf`, `Text/ByteSearch.gf`, `Text/CString.gf` and ordinary Span dependencies
with this optional source. Existing char-based splitting has no new dependency.
There is no new mutable-receiver family or multi-part iterator in this change.

```csharp
using std::Text;
using std; using std::Memory;

int main()
{
    Span<readonly(char)> text = new("key::value::rest", 16);
    Span<readonly(char)> key = new();
    Span<readonly(char)> value = new();
    if (!text.TrySplitOnce("::", ref key, ref value)) return 1;
    if (key.Length != 3 || value.Length != 11) return 2;
    return 0;
}
```

For the last complete match, include
`core/Text/text_split_last_once_pattern_span.gf` and call `TrySplitLastOnce`.
It accepts the same counted-pointer, C-string, and separator-span forms and
reuses `ByteSearch::LastIndexOf`. Overlapping matches are considered:
`"aaaaa"` split on `"aaa"` gives `"aa"` and an empty suffix.

An empty separator matches at the **end**: `before` retains the complete receiver
metadata and `after` is a default empty span. Empty source and separator spans,
and counted zero-length separator pointers, are accepted without reading their
data. Missing separators clear both outputs. The same lifetime, no-allocation,
and distinct-output requirements apply; receiver or separator metadata may be
reused as either output. Both result views are computed before either assignment.
The optional pattern file has the same source dependencies as first-match splitting.

```csharp
using std::Text;
using std::Memory;

int main()
{
    String qualified = s"std::Text::String";
    Span<readonly(char)> owner = new();
    Span<readonly(char)> name = new();
    bool found = qualified.AsReadOnly().TrySplitLastOnce("::", ref owner, ref name);
    // owner is "std::Text" and name is "String".
    if (!found || owner.Length != 9 || name.Length != 6) return 1;
    return 0;
}
```

This owning String example additionally uses `Text/String.gf` and its dependencies,
plus the optional `Text/string_span.gf` view convenience. A caller-buffer span
does not require String.
