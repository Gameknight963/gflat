# Selecting an element by rank

`NthElement<T>(Span<T> values, nuint nth, compare)` rearranges a mutable span so
that `values[nth]` compares equal to the value at that position in a fully sorted
copy. Every earlier element is no greater than it, and every later element is
no less. Neither side is necessarily sorted. Equal values need not retain order
or identity at the selected rank.

This is useful for selecting a median or percentile, or separating the smallest
`k` values without ordering them all. The caller chooses the rank and any policy
for even-sized medians or percentiles; the algorithm returns no owning value.

```csharp
using std; using std::Collections;
int Compare(readonly(int)* left, readonly(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}
int main()
{
    int[7] data = [9, 1, 8, 2, 7, 3, 6];
    Span<int> values = new(data, 7);
    NthElement<int>(values, 3, Compare);
    if (values[3] != 6) return 1;
    for (nuint i = 0; i < 3; i++) if (values[i] > values[3]) return 2;
    for (nuint i = 4; i < values.Length; i++) if (values[i] < values[3]) return 3;
    return 0;
}
```

`nth == values.Length` is a no-op, including zero on an empty span. A larger
index throws `Exception("Selection index out of range", 3)` before comparing or
changing elements. A singleton's rank zero does not call the comparator.
These cases do not read element storage. To select the smallest `k` elements,
pass `k` and inspect the prefix of length `k`; passing the whole length needs no
rearrangement. The prefix itself is not sorted.

The comparator borrows `immutable(T)*` values and returns negative, zero or
positive, as for `Sort`. It must provide a consistent ordering, must not mutate
the elements/span through aliases, and must not retain borrowed element pointers
across callbacks. Floating-point ordering is chosen by that comparator; no
implicit NaN or signed-zero policy is added.

The algorithm uses iterative three-way partitioning around an element tracked
by index. Swaps update that index; it never copies an owning pivot or holds a
removed element across a comparison. After a bounded number of partition passes
it heapsorts the remaining active range with `Sort`. Worst-case work is
O(n log n) comparisons, with constant extra storage and no recursion. There is
no guarantee of worst-case linear selection or of a particular permutation.

Elements move through the existing `Swap`/`Sort` relocation rules. Noncopyable
owners are supported; no element copy constructor or destructor is invoked by
the algorithm. Managed-reference values retain the normal root-tracking rules.
The algorithm allocates no heap scratch storage; ordinary comparator and
collector-hook effects still apply. Comparator errors propagate, leaving the
span an initialized permutation that may already
be rearranged; there is no rollback or completed partition guarantee on failure.
The addresses of element slots stay fixed while the values stored there move.
Raw aliases inside values are not retargeted; obey the same borrowing rules as
other in-place algorithms.

Include optional `Collections/NthElement.gf` with `Collections/Algorithms.gf`,
Span and their normal dependencies. `Collections/DefaultOrdering.gf` adds
`NthElement(values, nth)` for `T : IComparable<T>`, using `Comparer<T>::Compare`;
include `Collections/Comparer.gf` and `IComparable.gf` for that convenience. The explicit
three-argument operation remains independent of those comparison contracts.
There is no median wrapper.
