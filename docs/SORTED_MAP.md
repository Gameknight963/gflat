# Sorted map

`std::SortedMap<K,V>` owns keys and values in a sorted contiguous buffer. Supply
a comparer borrowing two keys through `immutable(K)*`; return negative, zero, or
positive according to their order. Zero identifies equivalent keys, regardless
of other equality or hashing implementations.

```csharp
using std;

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

- `Add(key,value)` inserts or throws for an equivalent key. `TryAdd` returns
  false for a duplicate. Both destroy unused incoming arguments normally.
- `Set(key,value)` inserts or replaces the existing value while retaining the
  original stored key. Named copyable arguments copy; `move` transfers owners.
- `ContainsKey`, `Find`, `FindReadOnly`, `FindImmutable`, `Remove`, `Extract`, and the ref indexer accept
  borrowed key pointers. Missing lookups return null; the indexer throws.
- `Find` and the indexer allow changing mapped values. Keys are exposed only
  through immutable views, since changing their order would corrupt lookup.
- `FindReadOnly` accepts a shallow readonly map and returns `readonly(V)*?`:
  inline fields cannot change, but pointers and managed references in the value
  still allow changing their targets. `FindImmutable` accepts an immutable map,
  returns `immutable(V)*?`, and protects those targets too. Neither copies or
  allocates. An extracted `Entry` exposes the same contracts through
  `ValueReadOnly` and `ValueImmutable`; its `Key` always remains immutable.
- `Remove` destroys a matching key/value pair and shifts later entries left.
  `Clear` destroys all entries but retains capacity. `Reserve` reserves entry
  capacity; `Count`, `Capacity`, and `IsEmpty` report collection metadata.
- `Extract(key)` removes and returns the matching `Entry`, transferring both key
  and value without copying or destroying them. Missing keys throw. Extraction
  allocates no entry storage, retains capacity, and shifts later entries left.
  Comparisons complete before removal; the lookup may borrow the matching entry's
  key, but all such entry borrows become invalid after extraction.

The map supports noncopyable owning keys and values: buffer growth and shifts
relocate entries rather than invoking their copy constructors. Moving the map
transfers its buffer; it has no implicit deep copy. Raw pointer elements do not
own their pointees. Managed references retain their normal collector behavior.

Comparisons finish before any insertion, replacement, or removal changes the
map. A throwing comparer, failing argument copy, or failed allocation leaves
existing entries intact. Unused incoming owners are destroyed. `Reserve` and
relocation do not call the comparer. Comparers must not mutate the map through
another alias; their order must remain stable for stored keys.

## Iteration, invalidation, and cost

The nested `Enumerator` borrows storage and visits entries in comparer order.
`Current` returns an immutable entry pointer; `Key` and `ValueImmutable` are borrowed
immutable pointers. Reading `Current` before a successful `MoveNext` or after
exhaustion throws. An immutable map can also create an enumerator.

Insertion and removal can move entries, and growth can replace the entire buffer.
Treat these operations and capacity growth as invalidating all entry/value
pointers and iterators. `Clear` and destruction invalidate every borrow. Do not
move or structurally mutate the map while using an iterator. Replacing a mapped
value retains its entry address but destroys the previous value's resources.

Lookups use O(log n) comparisons. Insertion and removal also shift O(n) entries;
enumeration is O(n), with contiguous storage and no per-entry allocation. This
is useful for smaller ordered tables or read-heavy maps. `HashMap` offers a
different tradeoff when sorted iteration is unnecessary.
