# Filter values by their encounter index

Indexed `Where` accepts a `bool(T, nuint)*` predicate:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

int main()
{
    int[5] data = [8, 3, 6, 2, 9];
    Span<int> values = new(data, 5);
    Vector<int> selected = values.AsEnumerable()
        .Where((int value, nuint index) => index % 2 == 0 && value > 5).ToVector();
    if (selected.Count != 3) return 1;
    if (selected[0] != 8 || selected[1] != 6 || selected[2] != 9) return 2;
    return 0;
}
```

The index starts at zero and counts every successful upstream move, including
rejected values. A preceding filter changes which encounters are indexed.
A following filter keeps these indices.

## Evaluation

The predicate runs during `MoveNext()`. Reading an accepted `Current` reads
the upstream value again without repeating this predicate. An earlier Select
can therefore run for filtering and again when the result is read. `Count()`
tests predicates but skips that second accepted-value read.

If the getter or predicate throws, its encounter has consumed an index. A
subsequent move advances upstream again. An extra upstream value after index
`nuint` maximum throws `Sequence index overflow` and finishes the adapter.

The predicate takes `T` by value. Borrowed owners are copied for it. Accepted
output reads the source again rather than reusing that argument.
Use named functions for owning arguments, and avoid retaining pointers into
temporary argument storage. Callbacks follow noncapturing function-pointer
rules.

See [query ownership](QUERIES.md#ownership) for source lifetimes.
Sources: `Queries/IndexedWhereSource.gf` and
`Queries/enumerable_indexed_where.gf`.
