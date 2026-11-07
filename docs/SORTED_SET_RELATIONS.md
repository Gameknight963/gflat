# Comparing sorted sets

`SortedSet<T>` has these read-only relation methods. Each takes an
`immutable(SortedSet<T>)*` argument and borrows its elements.

| Method | Returns true when |
| --- | --- |
| `SetEquals(other)` | Both sets represent the same values |
| `IsSubsetOf(other)` | Every receiver value is represented in `other` |
| `IsProperSubsetOf(other)` | The receiver is a subset and `other` has an additional value |
| `IsSupersetOf(other)` | Every value in `other` is represented in the receiver |
| `IsProperSupersetOf(other)` | The receiver is a superset and has an additional value |
| `Overlaps(other)` | At least one value is shared |

The receiving set's comparer determines which values are equivalent.

```csharp
using Standard::Collections;

int Ascending(immutable(int)* left, immutable(int)* right) => *left - *right;
int Parity(immutable(int)* left, immutable(int)* right) => *left % 2 - *right % 2;

int main()
{
    SortedSet<int> selected = new(Ascending);
    selected.Add(0);
    selected.Add(2);
    SortedSet<int> all = new(Ascending);
    for (int i = 0; i < 4; i++) all.Add(i);
    if (!selected.IsProperSubsetOf(&all) || !all.IsProperSupersetOf(&selected)) return 1;
    if (!selected.Overlaps(&all) || selected.SetEquals(&all)) return 2;

    SortedSet<int> parity = new(Parity);
    parity.Add(0);
    parity.Add(1);
    if (!parity.SetEquals(&all) || parity.IsProperSubsetOf(&all)) return 3;
    if (all.SetEquals(&parity)) return 4;
    return 0;
}
```

## Different comparers

The example's parity set has two equivalence classes, even and odd. From its
perspective, `all` contains both classes and has no additional values. From
`all`'s perspective, the stored representatives 0 and 1 omit 2 and 3.
Consequently, relations between differently configured sets can be asymmetric.

The methods compare the values actually stored in `other`. Values discarded
as duplicates during its insertion are no longer available for comparison.
Several stored values in `other` can also represent one receiver equivalence
class, so raw counts alone do not establish equality or a proper relation.

An empty set is a subset of every set. Every set is a superset of the empty
set. An empty set overlaps no set. A set equals itself and is both a subset
and superset of itself, with neither relation proper.

## Ownership, cost, and errors

These methods do not copy, move, destroy, or allocate elements. They support
noncopyable owners and preserve borrowed element addresses. Comparers must
keep both sets and their ordering stable. Comparison errors propagate without
changing either set. A comparer can have its own allocation and side effects.

For sets using the same comparison function pointer, subset and superset
checks and their proper forms use linear ordered traversal. Equality also
uses linear traversal. Count and empty-set checks can finish earlier.
Overlap probes the larger set while enumerating the smaller one.

With different function pointers, subset checks scan the other set for each
receiver value using the receiver's comparer, taking up to quadratic time.
Superset and overlap checks use receiver binary searches for values from the
other set. Equality and proper relations can combine those passes. The
algorithms use constant auxiliary storage.

The methods are part of `Collections/SortedSet.gf`, with its existing
`SortedMap` dependency.
