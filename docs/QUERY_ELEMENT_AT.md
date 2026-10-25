# Query elements by position

`ElementAt(nuint index)` returns the element at a zero-based position. It
advances a fresh iterator only as far as that position and throws `Exception`
with code 3 when the sequence ends first. Even the largest `nuint` index is
handled without count overflow.

`TryElementAt(nuint index, ref T result)` instead returns false when the sequence
ends first and leaves the initialized output unchanged. On success it replaces
output with the selected element using ordinary value assignment and returns
true. It does not default-construct a `T` or provide a borrowed reference.

For a sequence whose element type is itself `readonly(T)` or `immutable(T)`,
use value-returning `ElementAt`. The current `ref T` output contract also
qualifies the destination and prevents assignment. `TryFirst`, `TryLast`, and
`TrySingle` share this limitation. A pointer element with a readonly pointee
does not by itself make the pointer's output slot readonly.

```csharp
using std; using std::Queries;

int main()
{
    int[3] values = [12, 24, 36];
    Span<int> source = new(values, 3);
    int second = source.AsEnumerable().ElementAt(1); // 24
    int output = 99;
    if (!source.AsEnumerable().TryElementAt(1, ref output) || output != 24) return 1;
    if (source.AsEnumerable().TryElementAt(3, ref output) || output != 24) return 2;
    return 0;
}
```

The terminal reads `Current` only for the selected position. In particular,
`source.Select(project).ElementAt(2)` calls `project` once, for that element;
an out-of-range index does not call it. An earlier `Where` may read values to
evaluate its predicate normally. This is sequential enumeration, including for
sources that also support indexing.

Both methods stop immediately after obtaining the selected element. Skipped
terminal elements do not have their `Current` read. `TryElementAt` also handles
the largest `nuint` index by decrementing only nonzero remaining indices; a finite
sequence can be exhausted without index wraparound.

Results follow ordinary value semantics: owning values such as `String` are
independent copies when read from a borrowed source. Readonly and immutable
element views retain their qualifications. Pointer results remain borrowed
addresses. Keep the source's backing storage alive and obey its normal mutation
rules throughout the call; returning an address does not extend its lifetime.

Constructed iterators and their owned fields are destroyed before returning or
propagating an exception. Iterator construction, advancement, element copying,
and callbacks may throw; their original exceptions propagate.

`TryElementAt` is potentially throwing, like `TryFirst`. Iterator construction,
advancement, `Current`, and element-copy failures before assignment preserve the
previous output value. A successful assignment destroys its old resources
normally. This follows the existing owning assignment and destructor contracts;
the method does not catch errors or reinterpret them as an absent index.

Projects selecting standard-library sources explicitly need
`std/core/Queries/EnumerableElementAt.gf` alongside their query and source
dependencies. Ordinary projects including the standard library get it directly.
