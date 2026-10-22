# Query elements by position

`ElementAt(nuint index)` returns the element at a zero-based position. It
advances a fresh iterator only as far as that position and throws `Exception`
with code 3 when the sequence ends first. Even the largest `nuint` index is
handled without count overflow.

```csharp
using std;

int main()
{
    int[3] values = [12, 24, 36];
    Span<int> source = new(values, 3);
    int second = source.AsEnumerable().ElementAt(1); // 24
    return 0;
}
```

The terminal reads `Current` only for the selected position. In particular,
`source.Select(project).ElementAt(2)` calls `project` once, for that element;
an out-of-range index does not call it. An earlier `Where` may read values to
evaluate its predicate normally. This is sequential enumeration, including for
sources that also support indexing.

Results follow ordinary value semantics: owning values such as `String` are
independent copies when read from a borrowed source. Readonly and immutable
element views retain their qualifications. Pointer results remain borrowed
addresses. Keep the source's backing storage alive and obey its normal mutation
rules throughout the call; returning an address does not extend its lifetime.

Constructed iterators and their owned fields are destroyed before returning or
propagating an exception. Iterator construction, advancement, element copying,
and callbacks may throw; their original exceptions propagate.

Projects selecting standard-library sources explicitly need
`std/core/Collections/EnumerableElementAt.gf` alongside their query and source
dependencies. Ordinary projects including the standard library get it directly.
