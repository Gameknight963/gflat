# Borrowed text splitting

`std/core/TextParts.gf` provides `std::TextParts`, splitting counted bytes on a
single-byte delimiter without copying or allocating. It takes a
`Span<readonly(char)>` and implements
`IEnumerable<Span<readonly(char)>, TextParts::Enumerator>`.

`GetEnumerator()` creates an independent iterator over the same source. Each
`Current` is a borrowed span, not an owning String. `Current` throws before the
first successful `MoveNext` and after iteration ends. Empty parts have length
zero and no readable element; do not dereference their Data pointer.

By default leading, consecutive, and trailing delimiters preserve empty parts.
An empty input yields one empty part. Set `removeEmpty` to true to skip empty
parts; empty input then yields none. Embedded zero bytes remain ordinary bytes,
and zero itself can be used as the delimiter. This is byte splitting, not a
Unicode code-point or multi-character delimiter operation.

`TextPartsSpan.gf` adds `Split(delimiter, removeEmpty = false)` extensions for
`Span<char>` and `Span<readonly(char)>`. These copy only the borrowed view's
metadata. `StringSplit.gf` separately adds the same convenience to an owning
String through a `ref immutable(String)` receiver, avoiding a deep copy and
rejecting temporary String receivers. Neither extension file changes String or
Span's own source dependencies.

```csharp
using std;
String text = s"first,,last,";
TextParts parts = text.Split(',');
foreach (Span<readonly(char)> part in parts)
{
    // part is a view into text, including two empty parts in this example.
}
```

The source storage must remain alive and at the same address for every use of
the sequence, its iterators, and its returned spans. In particular, destroying,
moving, or reallocating an owning String can invalidate them. A reference
receiver prevents one easy temporary-lifetime mistake; it is not general
lifetime analysis. Mutations to the underlying bytes are visible through
existing parts and can change subsequent splitting; TextParts is not a snapshot.

`AsEnumerable()` exposes the existing concrete query wrapper without changing
these borrowing rules. Direct `foreach` is sufficient for ordinary iteration.
