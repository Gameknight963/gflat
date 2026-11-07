# Copy sorted set elements into a span

`SortedSet<T>` provides optional `CopyTo(Span<T>)`,
`CopyTo(Span<T>, nuint destinationIndex)`, and `TryCopyTo(Span<T>)` extensions.
They copy values in comparer order, including descending order when the set
uses a descending comparer. Copying itself does not call the comparer.

```csharp
using Standard::Collections;
using Standard::Memory;

int Compare(immutable(int)* left, immutable(int)* right) => *right - *left;

int main()
{
    SortedSet<int> values = new(Compare);
    values.Add(4);
    values.Add(9);
    int[3] output = [77, 77, 77];
    Span<int> destination = new(&output[0], 3);
    values.CopyTo(destination, 1);
    if (output[0] != 77 || output[1] != 9 || output[2] != 4) return 1;
    if (values.TryCopyTo(destination.Slice(0, 1))) return 2;
    return 0;
}
```

Destination elements must already be initialized, and `T` must be copyable.
Owning values follow their usual copy semantics. The optional helpers leave
borrowed set operations available for noncopyable types.

`TryCopyTo` returns false without copying when the span is too short. `CopyTo`
throws instead. Destination indices are validated even when the set is empty.
The written destination range must not overlap the set's backing allocation,
including spare capacity. Overlap throws before any copy takes place.

Copying allocates no internal storage. Element copies can allocate or throw.
A failure preserves the source, failing destination slot, and later slots.
Earlier destination replacements remain completed. Copy operations must not
structurally mutate the source set.

Selective builds include `Collections/SortedSet.gf`, `Collections/SortedMap.gf`,
and `Collections/sorted_set_copy.gf` with their normal value and span
dependencies. Freestanding forms take an immutable set pointer first, for
example `CopyTo(&values, destination)` or `TryCopyTo(&values, destination)`.
