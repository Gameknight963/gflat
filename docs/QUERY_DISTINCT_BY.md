# Lazy distinct values by key

`DistinctBy(selector)` keeps the first whole value for each selected key,
preserving encounter order:

```cpp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

struct Row
{
    public int Id;
    public int Payload;
    public Row(int id, int payload) { Id = id; Payload = payload; }
}

int Id(Row value) => value.Id;

int main()
{
    Row[5] rows = [new Row(2, 20), new Row(1, 10), new Row(2, 99),
                   new Row(3, 30), new Row(1, 88)];
    Span<Row> source = new(rows, 5);
    Vector<Row> first = source.AsEnumerable().DistinctBy(&Id).ToVector();
    if (first.Count != 3) return 1;
    if (first[0].Id != 2 || first[0].Payload != 20) return 2;
    if (first[1].Id != 1 || first[1].Payload != 10) return 3;
    if (first[2].Id != 3 || first[2].Payload != 30) return 4;
    return 0;
}
```

The selector has type `K(T)*`. Keys require `IHashable` and `IEquatable<K>` with
stable, consistent behavior. Each iterator owns a set of seen keys, so memory
grows with the distinct keys rather than with every source element.

## Evaluation

Each move reads every candidate's `Current` once and calls the key selector
once, including for duplicates. It stops at the next unseen key.

Reading the accepted `Current` reads the upstream value again. It does not
repeat DistinctBy's key selector, but an earlier `Select` can run again. Values
must remain consistent at that source position. `Count()` and `Any()` still
evaluate candidate keys.

## Key ownership

Selectors receive values by copy. A returned key must own its data or borrow
storage that remains valid throughout iteration. It must not borrow storage
owned only by that temporary argument.

The iterator releases its keys on destruction, including early exit. After a
failed move, discard the iterator. Input and callback effects may already have
occurred. See [query ownership](QUERIES.md#ownership) for source lifetimes.

Sources: `Queries/enumerable_distinct_by.gf`, `Queries/DistinctBySource.gf`,
and [HashSet](HASH_COLLECTIONS.md) with its dependencies.
