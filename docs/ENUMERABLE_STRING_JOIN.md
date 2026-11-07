# Join a sequence of strings

An enumerable of `String` values supports `Join(separator)` and
`Join(separator, separatorCount)`. Both return a new owning string and enumerate
the input once, inserting separators between entries, including empty entries.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    Vector<String> words = new();
    words.Add(s"one");
    words.Add(s"");
    words.Add(s"three");
    String text = words.AsEnumerable().Join(", ");
    if (!text.Equals("one, , three")) return 1;
    return 0;
}
```

The counted separator supports embedded null bytes. The C-string overload
stops at its first null. Neither form reads the separator for empty input or
one entry. Keep separator storage valid and unchanged while joining, including
across iterator callbacks.

The result preserves all source bytes. An empty sequence returns an empty
string without allocating. Empty entries remain entries: joining three empty
strings with a comma produces two commas. Enumeration obtains each Current
once. Source strings returned by value follow ordinary copy or move semantics,
so borrowing iterators can incur string copies.

Input, copy, allocation, or iterator errors propagate. Partial output and iterator
resources are released, and an existing string used as an assignment destination
is preserved if joining fails. Output grows incrementally. This needs no
collection of intermediate strings, but does not promise one output allocation.
For a known span of strings, the [span join](STRING_JOIN.md) can precompute and
reserve the complete output size.

Selective builds include `Text/enumerable_string_join.gf` with the usual String
and enumerable dependencies. The freestanding forms take the sequence first:
`Standard::Text::Join(values, separator[, separatorCount])`.
