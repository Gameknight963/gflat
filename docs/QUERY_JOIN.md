# Joining queries by key

`outer.Join(inner, outerKey, innerKey, resultSelector)` produces a result for
every pair with equal keys. Results follow outer order. Each outer element's
matches follow their original inner order, including duplicates.

```csharp
using Standard::Queries;

int Key(int value) => value % 2;
int Pair(int outer, int inner) => outer * 10 + inner;

int main()
{
    var outer = new IntRange(0, 3).AsEnumerable();
    var inner = new IntRange(0, 4).AsEnumerable();
    var query = outer.Join(inner, Key, Key, Pair);
    int[6] expected = [0, 2, 11, 13, 20, 22];
    nuint index = 0;
    foreach (int value in query)
    {
        if (index >= 6 || value != expected[index]) return 1;
        index++;
    }
    if (index != 6) return 2;
    return 0;
}
```

The free function `Join(outer, inner, outerKey, innerKey, resultSelector)` has
the same behavior. Generic arguments are inferred from the queries and callbacks.
Load `Queries/JoinSource.gf` and `Queries/enumerable_join.gf` with `HashMap`,
`Vector`, and their ordinary dependencies.

## Evaluation and buffering

Query construction captures source views without enumerating them. Each cursor
first advances the outer iterator. An empty outer skips inner enumeration.
Otherwise, the cursor reads the entire inner sequence into an owning hash map
of value groups before producing its first result. An infinite inner sequence
cannot produce a result.

Each inner `Current` and inner key selector is read once per inner element.
If the lookup is empty, the cursor finishes without reading outer `Current`
or invoking its key selector. Otherwise, each visited outer value is retained
and its key is computed once. Reading result `Current` calls the result selector,
so reading it repeatedly calls that selector again.

The cursor owns its lookup and one reusable outer-value slot. Moving the cursor
preserves active matches. Buffered values, stored keys, and the retained outer
value are released when iteration finishes or fails. Early `break` or return
releases them when the cursor leaves scope. The lookup's backing table and
reusable outer slot retain capacity until cursor scope exit. Captured source
views also remain owned until then.

Re-enumerating builds a fresh lookup and observes the sources again. External
stateful sources retain their own replay behavior.

## Keys, ownership, and errors

Keys use `HashMap` semantics and must implement `IHashable` and `IEquatable<K>`.
Equal keys must have equal hashes. Key selectors can return fresh noncopyable
owners, which are moved into the lookup. Duplicate-key temporaries are released.

By-value key and result callbacks require copyable input values. Pointer
elements allow borrowing noncopyable objects. Buffered pointer values do not
extend pointee lifetimes. Keep source storage and borrowed pointees alive and
stable while enumerating. Returned borrowed values must not refer to callback
parameter temporaries. Result values can be move-only owners.

Source, callback, key hashing/equality, copy, and allocation errors propagate.
They invalidate `Current`, release buffered values, and make later `MoveNext`
calls return false. Consumed input is not rolled back. `Current` throws before
the first successful move and after the end.
