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
nullable value pointer; `FindImmutable` and `ContainsKey` work on immutable maps.
`FindReadOnly` works on a shallow `readonly` map and returns `readonly(V)*?`:
the value's inline fields cannot change, but its pointers and managed references
still permit changes to their targets. `FindImmutable` returns `immutable(V)*?`
and protects those targets too. Both views borrow the stored value without copies
or allocation; keys remain immutable under either contract.
The ref indexer throws when the key is absent and allows changing an existing
value. `Remove` destroys an entry; `Clear` destroys every entry but retains storage.

`Extract(key)` removes an entry and transfers its owning `HashMap<K,V>::Entry`
to the caller. Missing keys throw. Its `Key`, `Value`, `ValueReadOnly`, and `ValueImmutable` members
expose the transferred objects; noncopyable keys and values are supported.
`ValueReadOnly` uses a shallow readonly receiver and value pointer;
`ValueImmutable` uses an immutable receiver and value pointer.
A successful extraction copies no elements, allocates no storage, and destroys
neither transferred owner. The entry remains valid independently of later map
growth, clearing, or destruction. It is an owning value, not a node handle or a
promise of allocation-free reinsertion. Destroying it destroys its key and value.
Lookup/hash/equality failure leaves the map unchanged. Passing a key pointer
borrowed from this map is allowed; lookup finishes before the entry is moved.
Extraction invalidates borrows into that entry and leaves a tombstone so other
colliding keys remain findable. The collection retains its allocation/capacity.

Maps use open addressing with linear probing, tombstones, and a maximum 50% load.
Each entry caches its hash, so growth relocates owning values without calling
user hash or equality functions. Automatic growth doubles usable capacity;
explicit `Reserve` accepts the requested entry count. Tombstones are compacted
when too many buckets have been used. Failed allocation or a throwing key comparison
leaves the existing entries intact. Values are destroyed exactly once.

Enumeration borrows the map and has unspecified order. `HashMap<K,V>::Enumerator`
returns immutable entry pointers with `Key` and `ValueImmutable`; set enumerators
return immutable element pointers. Do not mutate, move, or destroy the collection
while enumerating. Insertion and `Reserve` may invalidate all borrowed pointers,
including when tombstone compaction keeps capacity unchanged; removal invalidates
pointers into the removed entry. Collections do not own objects pointed to by
pointer-valued elements.

## Readonly set relations

`HashSet<T>` provides `SetEquals`, `IsSubsetOf`, `IsSupersetOf`,
`IsProperSubsetOf`, `IsProperSupersetOf`, and `Overlaps`. Each takes a
`immutable(HashSet<T>)*` and works on an immutable receiver. They borrow keys rather
than copying them, and perform no allocations, so noncopyable owning keys work
as well. Both empty sets are equal and subsets of each other; proper relations
require different counts, and an empty set never overlaps another set.

Relations use the same hashing and equality rules as lookups. They short-circuit
when counts or an early matching/missing element determine the answer. A throwing
hash/equality callback leaves both sets unchanged. As with individual lookups,
callbacks must not mutate either set through another alias. Readonly map and set
enumeration also borrows existing entries; its invalidation rules are unchanged.

## Set mutations

`ExceptWith(immutable(HashSet<T>)* other)` removes matching keys from this set.
It borrows the other set's keys without copying them, so noncopyable owning keys
are supported. Passing the same set clears it. It allocates no collection storage;
removed owners are destroyed normally. The implementation iterates the distinct
source, rather than holding an iterator into its changing target.

`IntersectWith(immutable(HashSet<T>)* other)` removes keys absent from the other
set, retaining this set's existing owners for shared keys. It also supports
noncopyable keys and allocates no collection storage. Self-intersection does
nothing; intersection with an empty set clears the target. Its internal map
helper scans private buckets directly and leaves tombstones for removed entries;
it does not keep a public enumerator alive during structural mutation. Public
enumerator invalidation rules remain unchanged.

With `Collections/HashSetExtensions.gf` included and `using std;`,
`target.UnionWith(&source)` copies missing keys into the target. Existing keys are
checked before copying, and self-union does nothing. Added owning keys use their
normal copy constructors; the source stays unchanged. This is a separate
ref-receiver extension so merely using `HashSet<T>` or `ExceptWith` does not
require noncopyable keys to gain a copy constructor.

The same optional source adds `target.SymmetricExceptWith(&source)`: shared keys
are removed, and keys found only in the source are copied into the target. Existing
target-only owners are retained. Self-symmetric-difference clears the set. As with
union, instantiating this copying extension requires a copyable element type;
core intersection and difference remain available for noncopyable owners.

These mutations can make partial progress if a later callback, copy, or allocation
fails. A failing probe does not remove its current key; earlier completed removals
or additions remain. All surviving keys stay owned, and
a distinct source remains intact. Hash/equality/copy callbacks must not mutate either set
through other aliases. Structural mutation invalidates target iterators and key
borrows under the rules above. A borrowed source must stay alive for the call.
