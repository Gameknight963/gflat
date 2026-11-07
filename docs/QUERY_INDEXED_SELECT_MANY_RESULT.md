# Flatten indexed selections into results

The indexed result-selector `SelectMany` overload takes two callbacks:

- An inner-sequence selector receiving the outer value and its `nuint` index
- A result selector receiving the retained outer value and an inner value

```csharp
using Standard::Queries;

Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value, nuint index)
    => new IntRange((int)index * 10, value).AsEnumerable();

int Combine(int outer, int inner) => outer * 100 + inner;

int main()
{
    var query = new IntRange(0, 3).AsEnumerable().SelectMany(Expand, Combine);
    int[3] expected = [110, 220, 221];
    nuint at = 0;
    foreach (int value in query)
    {
        if (at >= 3 || value != expected[at]) return 1;
        at++;
    }
    if (at != 3) return 2;
    return 0;
}
```

Indices count the outer sequence after upstream query operators, including
elements selecting empty inner sequences. Each enumeration starts at zero.
Creating the query invokes neither callback. The inner selector runs once per
outer value. The result selector runs on each Current read, so repeated reads
can repeat its effects and produce separate owning results.

The active inner state retains an outer snapshot for the result selector.
Callbacks taking owning outer values by value can require copies. Results may
be fresh move-only values. The inner source remains at a stable address while
its iterator borrows it, using one active-state allocation per selected source.
Copies, source construction, and callbacks can require further allocations.

Input, callback, or allocation failure retires the active state and invalidates
Current. Later MoveNext calls return false. Outer iterator resources are released
when its scope ends. Index overflow throws before reading the outer value or
calling either callback.

See [indexed SelectMany](QUERY_INDEXED_SELECT_MANY.md) and the ordinary
[result-selector form](QUERY_SELECT_MANY.md). Selective builds include
`Queries/IndexedSelectManyResultSource.gf`,
`Queries/enumerable_indexed_select_many_result.gf`, and `Memory/Unique.gf`, with
the usual query dependencies. The free form is
`SelectMany(source, indexedSelector, resultSelector)`.
