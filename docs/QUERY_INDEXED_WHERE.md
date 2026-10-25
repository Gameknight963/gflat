# Filter values by their encounter index

The optional indexed `Where` overload accepts a `bool(T, nuint)*` predicate.
It can select every nth record or filter by original position without keeping
mutable state outside the query.

```csharp
using std;
int main()
{
    int[5] data = [8, 3, 6, 2, 9];
    Span<int> values = new(data, 5);
    Vector<int> selected = values.AsEnumerable()
        .Where((int value, nuint index) => index % 2 == 0 && value > 5).ToVector();
    if (selected.Count != 3) return 1;
    if (selected[0] != 8 || selected[1] != 6 || selected[2] != 9) return 2;
    return 0;
}
```

Each enumerator starts at zero and counts every successful upstream move,
including values rejected by the predicate. The index is assigned before the
upstream `Current` getter and predicate run. A preceding filter changes which
encounters are indexed; a following filter does not renumber them.

The predicate runs during `MoveNext`. A failing upstream move invalidates
`Current` without advancing the assigned index. If reading the input or
evaluating the predicate throws, the encounter has already consumed its index
and the adapter has no current result. Retrying `MoveNext` advances upstream
again; it does not retry the faulting predicate. These are ordinary `Where`
retry semantics, subject to the upstream cursor's own behavior.

An accepted `Current` delegates to the upstream getter on every read without
rerunning the predicate. Its getter can fail and be retried at the same
position. Nothing caches the input value, so a preceding projection can run
once for filtering and again when the accepted value is read. `Count()` still
tests predicates and reads their inputs, but does not reread accepted values.

The callback takes `T` by value. Borrowed owning values such as `String` are
copied for the predicate, and accepted output reads follow ordinary copy rules.
Fresh noncopyable input is not retained to reuse after the predicate consumes
it. Use named functions for owning by-value parameters; capturing closures are
not supported. Keep borrowed source owners alive, and do not retain pointers
into a temporary callback argument's owned storage.

The adapter uses no heap buffer. Normal source and iterator copies, factories,
getters and callbacks keep their ordinary allocation and cleanup behavior.
Each enumeration gets its own counter; a consumable file is not rewound.
Indices do not wrap: if upstream succeeds after index `nuint` maximum, the
adapter throws `Sequence index overflow` before reading the value or calling
the predicate. That upstream value is consumed, `Current` is invalid, and
later moves return false. EOF after the maximum valid index returns false
normally.

Selective builds add `Collections/IndexedWhereSource.gf` and
`Collections/EnumerableIndexedWhere.gf` to the ordinary Enumerable, iterator
and exception sources. The existing adapter for a one-parameter predicate is
unchanged.
