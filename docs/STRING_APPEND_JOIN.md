# Append joined strings

The optional `String.AppendJoin` methods accept a span or enumerable of strings,
followed by a C-string or counted separator. They append the joined bytes to
the existing destination.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    Vector<String> words = new();
    words.Add(s"one");
    words.Add(s"two");
    String output = s"items: ";
    output.AppendJoin(words.AsEnumerable(), ", ");
    if (!output.Equals("items: one, two")) return 1;
    return 0;
}
```

Separators go between entries, including empty entries. Counted separators
preserve null bytes, and C-string separators stop at the first null. Unused
separators are not read for zero or one entry.

Joining finishes in a temporary owning string before the destination changes.
Input strings and separators can therefore borrow the destination. Enumerable
callbacks can read it, but must not modify it. Source strings retain their
ordinary copy or move behavior, and enumerables are traversed once.

Copy, input, iterator, or allocation failure preserves the destination's bytes,
length, capacity, and existing buffer address. Temporary output and iterator
resources are released. On success, appending can grow the destination and
invalidate its buffer borrows. Empty joined output leaves it unchanged.

Staging requires storage for the joined bytes. Appending copies those bytes
into the destination and can require a second allocation. For direct output
with partial-write failure behavior, use [WriteJoin](ENUMERABLE_WRITER_JOIN.md).

For span input, include `Text/string_append_join.gf` and `Text/string_join.gf`.
For enumerable input, include `Text/string_append_join_enumerable.gf` and
`Text/enumerable_string_join.gf`, with their usual dependencies. Free functions
use `AppendJoin(ref target, values, separator[, separatorCount])`.
