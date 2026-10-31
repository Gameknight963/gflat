# Sorted map

`Standard::Collections::SortedMap<K,V>` owns keys and values in a sorted contiguous buffer. Supply
a comparer borrowing two keys through `immutable(K)*`. Return negative, zero, or
positive according to their order. Zero identifies equivalent keys, regardless
of other equality or hashing implementations.

```csharp
using Standard; using Standard::Collections;

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
  original stored key. Named copyable arguments copy. `move` transfers owners.
- `ContainsKey`, `Find`, `FindReadOnly`, `FindImmutable`, `Remove`, `Extract`, and the ref indexer accept
  borrowed key pointers. Missing lookups return null. The indexer throws.
- `Find` and the indexer allow changing mapped values. Keys are exposed only
  through immutable views, since changing their order would corrupt lookup.
- `FindReadOnly` accepts a shallow readonly map and returns `readonly(V)*?`:
  inline fields cannot change, but pointers and managed references in the value
  still allow changing their targets. `FindImmutable` accepts an immutable map,
  returns `immutable(V)*?`, and protects those targets too. Neither copies or
  allocates. An extracted `Entry` exposes the same contracts through
  `ValueReadOnly` and `ValueImmutable`. Its `Key` always remains immutable.
- `Remove(key)` destroys a matching key/value pair and shifts later entries left,
  returning false when absent.
  `Clear` destroys all entries but retains capacity. `Reserve` reserves entry
  capacity. `Count`, `Capacity`, and `IsEmpty` report collection metadata.
- `Extract(key)` removes and returns the matching `Entry`, transferring both key
  and value without copying or destroying them. Missing keys throw. Extraction
  allocates no entry storage, retains capacity, and shifts later entries left.
  Comparisons complete before removal. The lookup may borrow the matching entry's
  key, but all such entry borrows become invalid after extraction.

The map supports noncopyable owning keys and values: buffer growth and shifts
relocate entries rather than invoking their copy constructors. Moving the map
transfers its buffer. It has no implicit deep copy. Raw pointer elements do not
own their pointees. Managed references retain their normal collector behavior.

`Remove(key, ref output)` transfers the mapped value into initialized output and
returns true. It destroys the removed key and the previous output. A missing key
returns false with output unchanged. The operation copies no owners, allocates
no storage, and retains capacity. All comparisons finish before any movement,
so comparison failure preserves the map and output. The lookup key may borrow
an independently stored part of the previous output until comparisons finish.
Lookup callbacks must not mutate the map, key, or output through another alias.

Output must not refer inside the backing allocation, including spare capacity.
For a zero-sized mapped type, its exact end address is also excluded: the last
entry's zero-sized value can have that address. This is a conservative address
restriction, rather than a proof of allocation identity. A successful lookup
rejects those output addresses before mutation. An absent lookup does not
inspect output storage.

Survivors are shifted, `Count` updated, and output replaced before the removed
key and old output are destroyed. Cleanup may inspect that resulting state,
but must not structurally mutate, move, or destroy the map/output or invalidate
its remaining storage through another alias. Destructor exceptions follow their
normal contract. Successful removal invalidates entry borrows as described below.

Comparisons finish before any insertion, replacement, or removal changes the
map. A throwing comparer, failing argument copy, or failed allocation leaves
existing entries intact. Unused incoming owners are destroyed. `Reserve` and
relocation do not call the comparer. Comparers must not mutate the map through
another alias. Their order must remain stable for stored keys.

## Iteration, invalidation, and cost

The nested `Enumerator` borrows storage and visits entries in comparer order.
`Current` returns an immutable entry pointer. `Key` and `ValueImmutable` are borrowed
immutable pointers. Reading `Current` before a successful `MoveNext` or after
exhaustion throws. An immutable map can also create an enumerator.

Insertion and removal can move entries, and growth can replace the entire buffer.
Treat these operations and capacity growth as invalidating all entry/value
pointers and iterators. `Clear` and destruction invalidate every borrow. Do not
move or structurally mutate the map while using an iterator. Replacing a mapped
value retains its entry address but destroys the previous value's resources.

Lookups use O(log n) comparisons. Insertion and removal also shift O(n) entries.
Enumeration is O(n), with contiguous storage and no per-entry allocation. This
is useful for smaller ordered tables or read-heavy maps. `HashMap` offers a
different tradeoff when sorted iteration is unnecessary.

## Borrowed key ranges

`GetRange(lowerInclusive, upperExclusive)` returns a
`Span<immutable(SortedMap<K,V>::Entry)>` covering a half-open key interval in the
map's comparer order. Both bounds borrow keys as `immutable(K)*`, and the method
accepts immutable or readonly map receivers. A stored key belongs to the result
when it compares greater than or equal to the lower bound and less than the
upper bound. Comparer-equivalent keys follow that same rule. Bounds need not
exist in the map. Equal or reversed bounds, an interval containing no keys, and
an empty map produce a default empty span (null `Data`, zero `Length`). With a
descending comparer, the lower bound comes first in that descending order.

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

The span borrows entries without copying keys or values, constructing owners, or
allocating container storage. It supports noncopyable owners and uses the existing
Span operations for indexing, slicing, enumeration, and queries. Keys and mapped
values are immutable through this view. Bounds are needed only during the call.
They may borrow stored keys, provided the map remains stable during comparisons.

The method performs two binary lower-bound searches, then slices the existing
storage. Comparers may allocate or throw. A thrown exception propagates without
changing the map. A comparer must not mutate the map or its keys through another
alias. The result has the same invalidation rules as ordinary entry iteration:
insertion, removal, growth, clearing, destruction, or moving the map invalidate
the view. Replacing a mapped value with `Set` retains its entry address while
destroying the previous value's resources, so do not retain derived resource
borrows across replacement. Keep the owning map alive throughout use of the span.
No additional Standard source files are required beyond the ordinary SortedMap/Span
dependencies.

## Borrowing the stored equivalent key

`FindKey(immutable(K)* query)` returns `immutable(K)*?` borrowing the actual
comparison-equivalent stored key, or null when absent. This is distinct from
Find, which returns the mapped value. It uses the existing logarithmic lookup,
without copying owners, allocating or modifying entries. Comparator exceptions
propagate under the existing callback rules. A query may borrow a stored key.
The result protects reachable key state but does not extend storage lifetime.
Keep the responsible owner alive. Growth can relocate all entries, and insertion
or removal can shift entries at or after its position. Reacquire affected borrows
before later use. Duplicate insertion and existing-key Set retain the canonical
stored key, which can carry payload different from the equivalent query.
