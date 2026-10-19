# Hash collections

```csharp
using std;

HashMap<String, int> counts = new();
counts.Add(s"apple", 1);
String key = s"apple";
counts[&key]++;
int*? found = counts.Find(&key);

HashSet<String> names = new();
names.Add(s"apple");
bool present = names.Contains(&key);
```

`HashMap<K,V>` owns its keys and values; `HashSet<T>` owns its elements. Insertions
take values, so use `move` when transferring an existing noncopyable object.
`TryAdd`/set `Add` return false for duplicates and destroy the unused arguments.
Map `Add` throws on duplicates; `Set` inserts or replaces the value while keeping
an existing key. `Reserve(n)` reserves room for at least `n` entries; `Capacity`
reports that usable entry count, matching the other containers.

Keys implement both `IHashable` and `IEquatable<K>`. The current generic syntax
allows one explicit constraint, so equality is checked through a constrained
helper when specializing the collection. Primitive values and `String` have these
implementations. Enums and pointers do not yet have built-in implementations.
Equal keys must have equal hashes, and their equality/hash must remain stable
while stored. Callbacks must not mutate the collection through another alias.
Default hashes are deterministic and not resistant to malicious
collision attacks.

Lookups accept a borrowed key pointer and never copy it. `Find` returns a borrowed
nullable value pointer; `FindReadOnly` and `ContainsKey` work on readonly maps.
The ref indexer throws when the key is absent and allows changing an existing
value. `Remove` destroys an entry; `Clear` destroys every entry but retains storage.

Maps use open addressing with linear probing, tombstones, and a maximum 50% load.
Each entry caches its hash, so growth relocates owning values without calling
user hash or equality functions. Automatic growth doubles usable capacity;
explicit `Reserve` accepts the requested entry count. Tombstones are compacted
when too many buckets have been used. Failed allocation or a throwing key comparison
leaves the existing entries intact. Values are destroyed exactly once.

Enumeration borrows the map and has unspecified order. `HashMap<K,V>::Enumerator`
returns readonly entry pointers with `Key` and `ValueReadOnly`; set enumerators
return readonly element pointers. Do not mutate, move, or destroy the collection
while enumerating. Insertion and `Reserve` may invalidate all borrowed pointers,
including when tombstone compaction keeps capacity unchanged; removal invalidates
pointers into the removed entry. Collections do not own objects pointed to by
pointer-valued elements.
