# Group query results by key

`ToLookup(keySelector)` returns an owning `HashMap<K, Vector<T>>`.
`ToLookup(keySelector, valueSelector)` groups projected values in
`HashMap<K, Vector<V>>`.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

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

Groups keep source encounter order. Map iteration order is unspecified.
Equal keys join the same group, with the first stored key representing it.
Keys require stable `IHashable` and matching `IEquatable<K>` behavior.

## Selectors and results

Each element's `Current` is read once, then the key selector runs, followed by
the optional value selector. Their types are `K(T)*` and `V(T)*`. Arguments
follow value-copy rules. A selected key or value must not borrow storage owned
only by that temporary argument.

The map indexer returns a reference to its vector. `lookup[&key].AsSpan()`
therefore borrows group elements. Keep the map and group storage valid while
using that span. Use `Find` when a key may be absent. The indexer throws for a
missing key.

An empty source produces an empty map. If iteration, callbacks, or allocation
fail, the partial result is cleaned up. Consumed input and callback effects
remain visible. See [query ownership](QUERIES.md#ownership).

Source: `Queries/enumerable_to_lookup.gf`, with HashMap and Vector dependencies.
