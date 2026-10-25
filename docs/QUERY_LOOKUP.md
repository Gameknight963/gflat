# Materialize ordered groups

`ToLookup(keySelector)` eagerly consumes the query and returns an owning
`HashMap<K, Vector<T>>`. `ToLookup(keySelector, valueSelector)` returns
`HashMap<K, Vector<V>>`. This is a plain map of vectors: map enumeration order is
unspecified, while each group's vector follows source encounter order. A missing
key behaves like any other missing HashMap key; there is no lazy lookup wrapper.

```csharp
using std; using std::Queries; using std::Collections;

int Parity(int value) => value % 2;
long Square(int value) => (long)value * (long)value;

int main()
{
    int[5] values = [3, 2, 5, 4, 1];
    Span<int> source = new(&values[0], 5);
    HashMap<int, Vector<int>> grouped = source.AsEnumerable().ToLookup(&Parity);
    HashMap<int, Vector<long>> projected = source.AsEnumerable().ToLookup(&Parity, &Square);
    int odd = 1;
    Span<int> originals = grouped[&odd].AsSpan();
    Span<long> squares = projected[&odd].AsSpan();
    if (originals.Length != 3 || originals[0] != 3 || originals[2] != 1) return 1;
    if (squares.Length != 3 || squares[0] != 9l || squares[1] != 25l) return 2;
    return 0;
}
```

Selectors use ordinary by-value signatures `K(T)*` and `V(T)*`. Keys require
`IHashable` and matching `IEquatable<K>`, as with HashMap. Hash and equality must
agree, and stored keys must keep stable identity. Equal keys group together;
the first stored key remains the representative, and later temporary keys are
destroyed normally. They do not trigger duplicate-key errors.

Each source element reads terminal `Current` once into a local value, invokes
the key selector, then invokes the optional value selector. Callback arguments
follow normal copy/move rules. The one-selector overload moves the original local
element into its group, not the callback argument copy. Selected keys and values
also move into their owning containers. A new group's first value is staged in
a local vector before inserting that group into the map. Internal map-value
pointers are never retained across map insertion or selector calls. Ordinary
allocator contracts still apply when a group's vector grows.

The result owns its keys and vectors. Borrowed fields inside a key or grouped
value keep their usual backing-owner lifetime obligations; do not select a view
borrowing a temporary selector argument. Source/query values follow ordinary
copy/move rules, and borrowed adapters still require their backing owners alive.
Callbacks must respect the source's normal mutation and lifetime rules.

The map indexer returns a reference to its vector, so `lookup[&key].AsSpan()`
borrows the group's elements without copying the vector. Keep the map alive and
avoid changes that invalidate that group or its storage while using the span.
Use `Find` when the key may be absent; the indexer throws for a missing key.

An empty source returns an empty map without internal allocation or selector
calls. A source iterator can itself allocate. Nonempty inputs allocate map and
group-vector storage and can allocate for element/selector argument copies.
Capacity and size limits follow the existing HashMap and Vector contracts.

Iterator, selector, hashing, equality, capacity, and allocation failures propagate.
The partial map, staged group, local element/key/value and iterator are destroyed
normally. Failed assignment preserves an existing destination map. Consumed
input and callback side effects are not rolled back.

For selective sources include `Queries/enumerable_to_lookup.gf`, the Enumerable
interfaces, and the existing HashMap/Vector hashing and equality dependencies.
