# Split borrowed text once on a byte-sequence delimiter

`Span<readonly(char)>` supports multi-byte separators for `TrySplitOnce` and
`TrySplitLastOnce`:

| Separator form | Call arguments |
| --- | --- |
| Counted bytes | `separator, count, ref before, ref after` |
| C string | `separator, ref before, ref after` |
| `Span<readonly(char)>` | `separatorSpan, ref before, ref after` |

`TrySplitOnce` uses the first exact match:

```csharp
using Standard::Text;
using Standard;
using Standard::Memory;

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

`TrySplitLastOnce` uses the last match, including overlapping matches.
For example, `"aaaaa"` split on `"aaa"` gives `"aa"` and an empty suffix.

```csharp
using Standard::Text;
using Standard::Memory;

int main()
{
    String qualified = s"Standard::Text::String";
    Span<readonly(char)> owner = new();
    Span<readonly(char)> name = new();
    bool found = qualified.AsReadOnly().TrySplitLastOnce("::", ref owner, ref name);
    // owner is "Standard::Text" and name is "String".
    if (!found || owner.Length != 14 || name.Length != 6) return 1;
    return 0;
}
```

| Case | Result |
| --- | --- |
| Found | True, with views excluding the separator |
| Missing | False, with both outputs reset to empty |
| Empty separator, first-match form | Empty `before`, complete input in `after` |
| Empty separator, last-match form | Complete input in `before`, empty `after` |

Use two distinct output variables. Either can reuse input or separator-span
metadata. Input and separator bytes may overlap. Keep the input owner alive for
the returned views.

Sources: `core/Text/text_split_once_pattern_span.gf`,
`text_split_last_once_pattern_span.gf`, and `Text/ByteSearch.gf`.
