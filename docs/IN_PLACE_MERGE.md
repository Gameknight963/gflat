# Merge adjacent sorted ranges

`InPlaceMerge<T>(values, middle)` stably merges two sorted ranges within one
mutable span. The first range is `[0, middle)` and the second is
`[middle, Length)`. Both must already follow the same ordering.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[6] data = [1, 4, 8, 2, 4, 9];
    Span<int> values = new(data, 6);
    InPlaceMerge<int>(values, 3);
    if (data[0] != 1 || data[1] != 2 || data[2] != 4 ||
        data[3] != 4 || data[4] != 8 || data[5] != 9) return 1;
    return 0;
}
```

The two-argument overload uses `IComparable<T>`. Pass an
`int(immutable(T)*, immutable(T)*)*` comparator as the third argument for another
ordering. Equivalent elements retain their relative order, with elements from
the first range preceding equivalent elements from the second.

The algorithm allocates no temporary buffer. It uses binary searches and rotations,
with O(n log n) relocations and O(log n) stack storage. Elements are relocated
rather than copied, so noncopyable owners are supported. Use `MergeSorted` when
the inputs should remain unchanged and an owning result is needed.

`middle` must not exceed `Length`. An invalid boundary throws before inspecting
or changing elements. An empty range returns without invoking the comparator.
If the comparator throws, the span may be partially rearranged, but each element
remains initialized and owned. Restrict the operation to a subrange with `Slice`.
