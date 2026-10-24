# Disjoint sets

`std::DisjointSet` tracks groups of integer indices. It is useful for undirected
graph connectivity, cycle detection, and deciding which edges join separate
components in Kruskal's minimum-spanning-tree algorithm.

```csharp
using std;
int main()
{
    DisjointSet groups = new(6);
    if (!groups.Union(0, 1) || !groups.Union(4, 5)) return 1;
    if (groups.Union(1, 0)) return 2;
    immutable(DisjointSet)* view = &groups;
    if (view.ElementCount != 6 || view.SetCount != 4) return 3;
    if (!view.Connected(0, 1) || view.Connected(0, 4)) return 4;
    if (view.SizeOf(0) != 2 || view.Find(4) != view.Find(5)) return 5;
    return 0;
}
```

Construction with `count` creates the fixed universe `0..count-1`, initially
with one component per element. Default construction and `new(0)` create an
empty universe. `ElementCount` never changes; `SetCount` decreases when two
different components merge.

`Union(first, second)` returns true when it joins two components. It returns false
for an existing connection, including an element joined to itself. `Find(index)`
returns a representative index; `Connected(first, second)` compares components,
and `SizeOf(index)` returns the size of that element's component. Representatives
are arbitrary members of their components, may change after a union, and need
not be the smallest index. Do not persist them as stable external identifiers.

All indices must be less than `ElementCount`. Invalid indices throw `Exception`.
An invalid Union leaves the partition unchanged, including when only its second
argument is invalid. Every indexed operation on an empty set is invalid.

## Ownership and cost

The value owns two vectors and supports moving, not copying. Its destructor
releases the vectors. Construction reserves their storage; successful unions
and queries allocate nothing. As with other owning values, let the value leave
scope normally or explicitly transfer it with `move`.

Union attaches the smaller component's tree to the larger one. Find does not
compress paths, so queries are immutable and work through protected views
without hidden writes. Find, Connected, SizeOf, and Union take logarithmic time
in the worst case. Storage and construction time are linear in the element
count. Construction can fail through the normal allocation API; partially
initialized ownership is cleaned up by the language.

There is no insertion, removal, splitting, or concurrent mutation support.
Map application-specific labels to indices separately. The source is
`core/DisjointSet.gf`, using Vector and its ordinary runtime contracts.
