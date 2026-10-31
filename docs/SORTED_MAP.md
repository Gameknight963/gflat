# Sorted map

`Standard::Collections::SortedMap<K,V>` owns entries in a sorted contiguous buffer.
Supply a comparer returning negative, zero, or positive for key order. Zero
identifies equivalent keys.

```csharp
using Standard;
using Standard::Collections;

int Compare(readonly(int)* left, readonly(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    SortedMap<int, int> scores = new(Compare);
    scores.Add(20, 200);
    scores.Add(10, 100);
    int key = 10;
    scores[&key]++;
    SortedMap<int, int>::Enumerator entries = scores.GetEnumerator();
    entries.MoveNext(); // Key 10 is first.
    return *entries.Current.ValueImmutable - 101;
}
```

## Operations and ownership

| Operation | Effect |
| --- | --- |
| `Add(key, value)` | Inserts, throwing for an equivalent key |
| `TryAdd(key, value)` | Inserts, or returns false for a duplicate |
| `Set(key, value)` | Inserts or replaces the mapped value, retaining the original key |
| `Find`, `FindReadOnly`, `FindImmutable` | Mutable, shallow readonly, or deep immutable value pointer |
| Ref indexer | Borrows a value, throwing when absent |
| `Remove(key)` | Destroys the pair, returning false when absent |
| `Extract(key)` | Removes and returns an owning Entry, throwing when absent |
| `Clear()` | Destroys entries and retains capacity |
| `Reserve(count)` | Reserves entry capacity |

Lookup arguments are key pointers. Find variants return null when absent.
Insertion takes values, so use `move` for existing noncopyable owners. Duplicate
arguments are destroyed. The map supports noncopyable keys and values.

`Remove(key, ref output)` transfers the value into initialized output and destroys
its previous value. Absence preserves output. Output must be separate from the
map and backing allocation, including spare capacity. For zero-sized values, the
allocation's exact end address is also excluded.

Comparisons finish before mutation. Comparer or allocation failure preserves
existing entries. Callbacks must keep the map and key ordering stable. Removed
keys and old output are destroyed after map/output state is updated.

## Iteration, invalidation, and cost

Enumeration visits immutable Entry pointers in comparer order. Insertion, removal,
and growth can relocate entries. Reacquire borrows afterward. Replacing a value
retains its entry address but destroys the old value's resources.

Lookup takes O(log n) comparisons. Insertion and removal shift O(n) entries.
Keep the owner alive and structurally stable while iterating.

## Borrowed key ranges

`GetRange(lowerInclusive, upperExclusive)` returns
`Span<immutable(SortedMap<K,V>::Entry)>` for a half-open interval in comparer order.
Bounds need not exist. Equal, reversed, or unmatched bounds return an empty span.
With descending order, the lower bound is the earlier bound in that order.

```csharp
using Standard::Collections;
using Standard::Memory;

int Compare(immutable(int)* left, immutable(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    SortedMap<int,int> scores = new(Compare);
    scores.Add(10, 100); scores.Add(20, 200); scores.Add(30, 300);
    int lower = 15; int upper = 30;
    Span<immutable(SortedMap<int,int>::Entry)> selected = scores.GetRange(&lower, &upper);
    if (selected.Length != 1 || *selected[0].Key != 20) return 1;
    if (*selected[0].ValueImmutable != 200) return 2;
    return 0;
}
```

The range follows the same invalidation rules as entry pointers. Bounds may
borrow stored keys and are needed only during lookup.

## Borrowing the stored equivalent key

`FindKey(query)` returns an immutable pointer to the actual stored key, or null.
Equivalent query objects may have different payload fields. Duplicate insertion
and existing-key Set retain the first stored key.

Source: `core/Collections/SortedMap.gf`
