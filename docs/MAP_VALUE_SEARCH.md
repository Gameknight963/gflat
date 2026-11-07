# Searching map values

Load `Standard/core/Collections/hash_map_value_search.gf` or
`sorted_map_value_search.gf` for borrowed `ContainsValue` searches.
The default overload uses `IEquatable<V>`. A custom overload accepts
`bool(immutable(V)*, immutable(V)*)*` without imposing that value constraint.
Freestanding forms accept a pointer to the map first.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    HashMap<int, String> labels = new();
    labels.Add(1, s"ready");
    labels.Add(2, s"waiting");
    String wanted = s"ready";
    if (!labels.ContainsValue(&wanted)) return 1;
    wanted = s"absent";
    if (labels.ContainsValue(&wanted)) return 2;
    return 0;
}
```

Values and the searched value are borrowed, so searches support noncopyable
owners and do not copy stored values. Comparison receives the stored value
first and the search value second. The search argument may borrow a stored value while
the map stays unchanged. Searches stop on the first match; an empty map
returns false without invoking equality.

Sorted maps visit values in key order. Hash maps use their current unspecified
entry order. No key hashing, equality, or ordering callback is invoked.
Equality failures propagate without modifying the map. The comparator must
not structurally mutate or destroy the map during the search.

A sorted-map search takes at most `Count` value comparisons. A hash-map search
scans its backing buckets, skipping empty and deleted slots. Neither operation
allocates storage apart from work performed by the supplied comparison.
