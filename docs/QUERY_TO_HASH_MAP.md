# Materialize a query as a hash map

`ToHashMap(keySelector)` returns an owning `HashMap<K, T>`; each selected key is
paired with its original element. `ToHashMap(keySelector, valueSelector)` instead
returns `HashMap<K, V>`. Selectors have the ordinary by-value types `K(T)*` and
`V(T)*`. Keys require `IHashable` and the matching `IEquatable<K>` contract,
following HashMap's existing rules. Hash and equality must agree, and stored keys
must retain stable identity. Map enumeration order is unspecified.

```csharp
using std; using std::Collections;

int Key(int value) => value;
long Square(int value) => (long)value * (long)value;

int main()
{
    int[3] values = [2, 5, 9];
    Span<int> source = new(&values[0], 3);
    HashMap<int, int> originals = source.AsEnumerable().ToHashMap(&Key);
    HashMap<int, long> squares = source.AsEnumerable().ToHashMap(&Key, &Square);
    int lookup = 5;
    if (originals[&lookup] != 5 || squares[&lookup] != 25l) return 1;
    return 0;
}
```

Each element reads terminal `Current` once into a local value. The key selector
runs first. The two-selector overload then invokes the value selector using that
same local element. These by-value callback arguments follow ordinary copy/move
rules and may allocate or throw. The one-selector overload moves the original
local element into the map, not the callback's argument copy. Both overloads move
the selected key into the map; the second also moves its selected value.

Insertion uses `HashMap.Add`: an equal key throws
`Exception("Duplicate HashMap key")`. There is no overwrite or merge policy.
The value selector has already run for that duplicate element before Add detects
the duplicate. Callbacks and consumed input are not rolled back.

The returned map owns its keys and values. Borrowed fields inside them retain
their usual backing-owner lifetime requirements. Do not return a key or value
borrowing a temporary selector argument. The query receiver/source follow normal
value-copy/move rules; borrowed adapters still require their backing owners alive
and callbacks must respect the source's existing mutation rules.

An empty source returns an empty map without internal allocation or selector
calls. Its own iterator construction/advancement may still allocate. Other inputs
allocate map storage and may allocate while copying elements or creating results.
There is no initial capacity guess or retained source/iterator in the result.

Iterator, selector, hashing, equality, capacity, allocation, and duplicate-key
failures propagate. Locals, partial maps, and iterators are destroyed normally.
Failed assignment to an existing map preserves that destination; external side
effects and source advancement remain visible.

For explicit source selection include `Collections/EnumerableToHashMap.gf`, the
normal Enumerable interfaces, and HashMap's existing hashing/equality dependencies.
