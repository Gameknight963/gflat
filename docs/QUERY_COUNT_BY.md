# Count elements by key

`CountBy(selector)` returns an owning `HashMap<K, nuint>` with a count for each
selected key. The selector has type `K(T)*`.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

int Parity(int value) => value % 2;

int main()
{
    int[6] values = [1, 2, 3, 2, 4, 1];
    Span<int> source = new(&values[0], 6);
    HashMap<int, nuint> counts = source.AsEnumerable().CountBy(&Parity);
    int odd = 1;
    int even = 0;
    if (counts.Count != 2 || counts[&odd] != 3 || counts[&even] != 3) return 1;
    return 0;
}
```

The operator consumes the complete query. For each element it reads `Current`
once, then calls the selector once. Equal keys share a counter, with the first
stored key representing that group. Map iteration order is unspecified.

Keys must implement `IHashable` and `IEquatable<K>`. Equal keys need equal
hashes, and stored keys must keep stable hash and equality behavior. A selected
key must own its data or borrow storage that stays valid for the result map.
In particular, it must not borrow the selector's temporary by-value argument.

Empty input returns an empty map. Incrementing a counter past `nuint` maximum
throws `Exception("Sequence count overflow")`. A failed operation cleans up its
partial map, while consumed input and callback effects remain visible.

See [hash collections](HASH_COLLECTIONS.md) and [query ownership](QUERIES.md#ownership).
The extension source is `Queries/enumerable_count_by.gf`.
