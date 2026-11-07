# Reserving and trimming hash tables

`HashMap<K, V>` and `HashSet<T>` expose `EnsureCapacity(nuint requested)`.
It reserves room for at least that many entries and returns the resulting
entry capacity, rather than the number of internal buckets.

```csharp
using Standard::Collections;

int main()
{
    HashMap<int, int> scores = new();
    if (scores.EnsureCapacity(100) < 100) return 1;
    scores.Add(7, 42);
    scores.TrimExcess(10);
    if (scores.Capacity != 10) return 2;
    scores.TrimExcess();
    int key = 7;
    if (scores[&key] != 42) return 3;
    scores.Clear();
    scores.TrimExcess();
    if (scores.Capacity != 0) return 4;
    return 0;
}
```

`TrimExcess()` trims for the current `Count`. `TrimExcess(requested)` retains
room for the requested entry count and throws if it is below `Count`.
Neither form grows a table. Nonempty tables retain a minimum capacity of four
entries. Trimming an empty table to zero releases its storage.

Trimming relocates owners without copying them and uses stored hashes without
calling key hashing or equality methods. A successful relocation removes
deletion holes and invalidates borrowed keys, values, and enumerators. Enumeration
order can change. Allocation failure preserves the existing table and its entries.
Relocation scans the old buckets, initializes the new buckets, and probes
for each retained entry. Heavy collisions can make probing quadratic in
the number of entries.

`Clear()` retains allocation. `Reserve()` remains available when the resulting
capacity is not needed. Both API families operate on entry counts.
