# Collect a query into a hash map

| Method | Result |
| --- | --- |
| `ToHashMap(keySelector)` | `HashMap<K, T>` pairing each key with its source element |
| `ToHashMap(keySelector, valueSelector)` | `HashMap<K, V>` with projected values |

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

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

The operator consumes the complete query. Keys require stable `IHashable` and
matching `IEquatable<K>` behavior. Map iteration order is unspecified.

For each element, `Current` is read once, followed by the key selector and
optional value selector. Their types are `K(T)*` and `V(T)*`. The original
element goes into the one-selector result. Callback arguments follow value
copying rules.

## Duplicate keys and failures

An equal key throws `Exception("Duplicate HashMap key")`. The value selector
has already run for that element before insertion detects the duplicate.
Use [ToLookup](QUERY_LOOKUP.md) to collect groups instead.

A failed operation cleans up its partial map, while consumed input and callback
effects remain visible. Keys and values must own their data or borrow external
storage that stays valid. They must not borrow the selector's temporary
by-value argument. See [query ownership](QUERIES.md#ownership).

Source: `Queries/enumerable_to_hash_map.gf`, with HashMap dependencies.
