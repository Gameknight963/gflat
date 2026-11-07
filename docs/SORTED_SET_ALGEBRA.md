# Mutating sorted sets

`SortedSet<T>` provides these set operations. `other` is an
`immutable(SortedSet<T>)*`, and the receiving set's comparer defines equality.

| Method | Effect |
| --- | --- |
| `UnionWith(other)` | Add values absent from the receiver |
| `IntersectWith(other)` | Remove receiver values not represented in `other` |
| `ExceptWith(other)` | Remove receiver values represented in `other` |
| `SymmetricExceptWith(other)` | Keep values represented in exactly one input |
| `RemoveWhere(predicate)` | Remove matching values and return the removed count |

`RemoveWhere` takes `bool(immutable(T)*)*` and visits values in comparer order.

```csharp
using Standard::Collections;

int Compare(immutable(int)* left, immutable(int)* right) => *left - *right;
bool Even(immutable(int)* value) => *value % 2 == 0;

int main()
{
    SortedSet<int> values = new(Compare);
    values.Add(1);
    values.Add(2);
    SortedSet<int> other = new(Compare);
    other.Add(2);
    other.Add(3);
    values.UnionWith(&other); // 1, 2, 3
    if (values.Count != 3 || values.RemoveWhere(Even) != 1) return 1;
    values.SymmetricExceptWith(&other); // 1, 2
    int one = 1;
    int two = 2;
    if (values.Count != 2 || !values.Contains(&one) || !values.Contains(&two)) return 2;
    values.IntersectWith(&other); // 2
    if (values.Count != 1 || !values.Contains(&two)) return 3;
    values.ExceptWith(&other);
    if (!values.IsEmpty || other.Count != 2) return 4;
    return 0;
}
```

## Comparers and representatives

The [receiver's equality rules](SORTED_SET_RELATIONS.md#different-comparers)
apply when the two sets use different comparers. Multiple values stored in
`other` can represent one receiver equivalence class. Symmetric difference
processes that class once, rather than toggling it repeatedly.

Union retains existing receiver objects for duplicate values. Newly inserted
values are copies of the first applicable representative in `other`'s iteration
order. Intersection and difference retain the receiver's surviving objects.
A distinct `other` set is unchanged.

Union and intersection with the same object leave it unchanged. Difference
and symmetric difference with the same object clear it.

## Ownership and failure behavior

`UnionWith` and `SymmetricExceptWith` require copyable elements. They are kept
in `Collections/sorted_set_algebra.gf`, with freestanding forms such as
`UnionWith(ref target, other)`. Only absent values are copied for insertion.

`ExceptWith`, `IntersectWith`, and `RemoveWhere` are part of the base type and
support noncopyable owners. Removed values are destroyed once. Keep borrowed
source storage alive and stable, and reacquire receiver element references
and iterators after mutation.

Operations commit changes as they proceed. A comparison, predicate, copy, or
allocation error propagates with earlier completed changes retained. The
receiver remains a valid sorted set. `RemoveWhere` preserves the failing value
and unvisited suffix after removing earlier matches. Callbacks must leave
both sets and key ordering unchanged.

## Storage and cost

Erasing operations do not allocate internal storage. Copying operations can
allocate element copies or grow the receiver. With different comparison
function pointers, symmetric difference checks earlier source representatives
without allocating a temporary set, taking up to quadratic comparison work.
Sets sharing a comparison function skip that duplicate scan.

Storage is contiguous. Each insertion or removal can shift the remaining
entries, so a batch of changes, including `RemoveWhere`, can require quadratic
relocation work. Intersection with different comparers also scans source
values for each receiver key. Capacity is retained after erasure.
