# Disjoint sets

`Standard::Collections::DisjointSet` groups a fixed universe of indices, useful
for connectivity, cycle detection, and minimum-spanning-tree algorithms.

```csharp
using Standard;
using Standard::Collections;

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

Construction with count creates indices `0..count-1`, each initially in its own
component. Default construction creates an empty universe.

| Operation | Result |
| --- | --- |
| `ElementCount` | Fixed universe size |
| `SetCount` | Current number of components |
| `Union(first, second)` | True when joining separate components |
| `Find(index)` | A representative index |
| `Connected(first, second)` | Whether both belong to one component |
| `SizeOf(index)` | Number of elements in that component |

Indices must be below ElementCount. Invalid indices throw Exception and preserve
the partition. Representatives can change after union. Use application labels
separately when stable identity matters.

## Ownership and cost

The value is move-only. Storage and construction cost are linear in element
count. Union attaches the smaller tree to the larger. Queries and union take
O(log n) worst-case time. Queries accept immutable views.

## Reuse a fixed universe

`Reset()` restores singleton components and retains storage. This supports another
graph batch over the same indices:

```csharp
using Standard::Collections;

int main()
{
    DisjointSet groups = new(6);
    groups.Union(0, 1); groups.Union(1, 2);
    if (groups.SetCount != 4 || groups.SizeOf(0) != 3) return 1;

    // A new graph batch has the same vertices and different edges.
    groups.Reset();
    if (groups.ElementCount != 6 || groups.SetCount != 6) return 2;
    if (groups.Find(2) != 2 || groups.SizeOf(2) != 1) return 3;
    groups.Union(2, 5);
    if (groups.Connected(0, 2) || !groups.Connected(2, 5)) return 4;
    return 0;
}
```

Reset takes O(n). Previous component relationships and representative meanings
are discarded.

Source: `core/Collections/DisjointSet.gf`
