# Flatten with the outer index

`SelectMany` accepts a selector taking an outer value and its zero-based
`nuint` index. Each selected inner sequence is enumerated completely before
advancing to the next outer value.

```csharp
using Standard::Queries;

Enumerable<int, IntRange, IntRange::Enumerator> Expand(int value, nuint index)
    => new IntRange((int)index * 10, value).AsEnumerable();

int main()
{
    var flattened = new IntRange(0, 3).AsEnumerable().SelectMany(Expand);
    int[3] expected = [10, 20, 21];
    nuint at = 0;
    foreach (int value in flattened)
    {
        if (at >= 3 || value != expected[at]) return 1;
        at++;
    }
    if (at != 3) return 2;
    return 0;
}
```

Indices count outer elements, including elements selecting an empty inner
sequence. They restart for each enumeration. An upstream filter changes which
outer elements reach the operator, so indexing starts at zero after that filter.
The index does not count flattened results.

Creating the query does not invoke the selector or enumerate input. Each
iterator owns its traversal state and has one active inner sequence at a time.
The active inner source is held at a stable address while its iterator can
borrow it. This currently requires an allocation for each selected inner source,
including an empty one. Inner-source construction and copying may also allocate.

The selector runs once per outer value. Repeated Current reads delegate to the
active inner iterator and do not invoke the selector again. Input, selector,
allocation, or inner-iterator errors propagate and retire the active inner
state. After an error, MoveNext returns false and Current is invalid. Normal
scope cleanup releases the outer iterator. Index overflow throws before reading
the outer value or calling its selector.

For the ordinary overload and lifetime rules, see
[SelectMany](QUERY_SELECT_MANY.md). Selective builds include
`Queries/IndexedSelectManySource.gf`, `Queries/enumerable_indexed_select_many.gf`,
and `Memory/Unique.gf`, with the usual query dependencies. The free form is
`SelectMany(source, selector)`.

To combine each inner element with its outer value, use the
[indexed result-selector overload](QUERY_INDEXED_SELECT_MANY_RESULT.md).
