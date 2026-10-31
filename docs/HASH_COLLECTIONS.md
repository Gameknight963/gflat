# Hash collections

`Standard::Collections::HashMap<K,V>` owns keys and values. `HashSet<T>` owns
unique elements.

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

HashMap<String, int> counts = new();
counts.Add(s"apple", 1);
String key = s"apple";
counts[&key]++;
int*? found = counts.Find(&key);

HashSet<String> names = new();
names.Add(s"apple");
bool present = names.Contains(&key);
```

Keys implement IHashable and IEquatable. Primitive scalars and String work
directly. Hash and equality must remain consistent and stable while stored.

| Map operation | Effect |
| --- | --- |
| `Add(key, value)` | Inserts, throwing on a duplicate key |
| `TryAdd(key, value)` | Inserts, or returns false for a duplicate |
| `Set(key, value)` | Inserts or replaces the value, preserving an existing stored key |
| `Find(&key)` | Mutable value pointer, or null |
| `FindReadOnly(&key)` | Shallow readonly value pointer, or null |
| `FindImmutable(&key)` | Deep immutable value pointer, or null |
| Ref indexer | Borrows a value, throwing when absent |
| `Remove(&key)` | Destroys the entry, returning false when absent |
| `Clear()` | Destroys entries and retains capacity |
| `Reserve(count)` | Reserves space for that many entries |

Insertion takes values. Use `move` to transfer a noncopyable owner. Duplicate
arguments are destroyed. FindReadOnly protects inline value state, while
FindImmutable also protects reachable pointees. Stored keys remain immutable.

## Taking entries out

`Remove(key, ref output)` transfers the mapped value into initialized output.
A missing key returns false and preserves output. Success destroys the previous
output and removed key after updating the map.

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

int main()
{
    HashMap<int, String> names = new();
    names.Add(1, s"kept");
    int key = 1;
    String removed = s"old";
    if (!names.Remove(&key, ref removed) || !removed.Equals("kept") || !names.IsEmpty) return 1;
    if (names.Remove(&key, ref removed) || !removed.Equals("kept")) return 2;
    return 0;
}
```

Keep output separate from the map and its entire backing allocation, including
spare buckets. Lookup finishes before movement. Hash/equality failure preserves
the map and output.

`Extract(key)` returns an owning Entry containing both key and value. Missing
keys throw. The returned entry survives map growth or destruction and destroys
its contents when its own lifetime ends.

## Borrowing and iteration

Enumeration order is unspecified. Map iterators expose immutable Entry pointers.
Set iterators expose immutable element pointers. Keep the owner alive and avoid
structural changes while using an iterator.

Insertion and Reserve can invalidate every borrow, including when tombstones
are compacted without increasing capacity. Removal ends borrows into that entry.
Callbacks must not mutate the involved collection through another alias.

Maps use open addressing, linear probing, and a maximum 50% load. Capacity counts
usable entries. Growth relocates entries using cached hashes. Allocation or lookup
failure preserves existing entries.

## Readonly set relations

`SetEquals`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`,
`IsProperSupersetOf`, and `Overlaps` accept an immutable set pointer. They support
noncopyable keys. Empty sets are equal and subsets of each other. Proper relations
require different counts, and an empty set overlaps nothing.

## Set mutations

`HashSet.Add(value)` returns false for a duplicate and keeps the original owner.

| Method | Effect |
| --- | --- |
| `RemoveWhere(predicate)` | Removes matching values and returns their count |
| `ExceptWith(other)` | Removes shared values. Self-difference clears the set. |
| `IntersectWith(other)` | Retains shared values. Self-intersection preserves the set. |
| `UnionWith(other)` | Copies missing values. Self-union preserves the set. |
| `SymmetricExceptWith(other)` | Removes shared values and copies source-only values |

Union and symmetric difference require copyable values and live in
`Collections/hash_set_algebra.gf`. The other mutations support noncopyable owners.

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

bool Temporary(immutable(String)* key) => key.StartsWith("tmp:");
int main()
{
    HashSet<String> names = new();
    names.Add(s"tmp:build"); names.Add(s"saved");
    nuint removed = names.RemoveWhere(Temporary);
    if (removed != 1 || names.Count != 1) return 1;
    return 0;
}
```

A failing predicate preserves its current value and unvisited values. Earlier
removals remain committed. Other set mutations can likewise make partial progress
before a later hash, comparison, copy, or allocation error. Callbacks must leave
both collections structurally unchanged.

## Borrowing the stored equivalent key

`map.FindKey(&query)` and `set.Find(&query)` return immutable pointers to the actual
stored key/value, or null. This matters when equality uses an ID but the stored
value has a different payload:

```csharp
using Standard;
using Standard::Collections;

struct Key : IHashable, IEquatable<Key>
{
    public int Id;
    public int Payload;
    public Key(int id, int payload) { Id = id; Payload = payload; }
    public immutable nuint GetHashCode() => (nuint)Id;
    public immutable bool Equals(immutable(Key)* other) => Id == other.Id;
}

int main()
{
    HashSet<Key> values = new();
    values.Add(new Key(7, 70));
    Key query = new(7, 99);
    immutable(Key)*? stored = values.Find(&query);
    if (stored == null || stored!.Payload != 70 || stored == &query) return 1;
    Key missing = new(8, 80);
    if (values.Find(&missing) != null) return 2;
    return 0;
}
```

Reacquire pointers after growth or removal. Duplicate insertion and existing-key
Set preserve the original key.

Sources: `core/Collections/HashMap.gf`, `HashSet.gf`, and `hash_set_algebra.gf`
