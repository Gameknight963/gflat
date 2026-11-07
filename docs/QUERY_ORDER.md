# Order a query

`Order()` uses `IComparable<T>` to yield a stable ascending order.
`Order(compare)` takes `int(immutable(T)*, immutable(T)*)*` for another order.
`OrderDescending()` reverses the default ordering; `OrderDescending(compare)`
reverses a supplied comparison. Descending comparison swaps the callback's
arguments rather than negating its result. Equal-priority values keep their
original encounter order in either direction.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[4] data = [3, 1, 2, 1];
    Span<int> values = new(&data[0], 4);
    var ascending = values.AsEnumerable().Order();
    var descending = values.AsEnumerable().OrderDescending();
    if (ascending.First() != 1 || descending.First() != 3) return 1;
    if (data[0] != 3 || data[1] != 1) return 2;
    return 0;
}
```

## Add a lower-priority key

`ThenBy(selector)` and `ThenByDescending(selector)` order ties under the value
comparison. They do not enumerate the `Order` result a second time. The combined
cursor snapshots the original source once and computes each selected key once
per element. Additional key levels can be chained in either direction.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

struct Record : IComparable<Record>
{
    public int Category;
    public int Id;

    public Record(int category, int id)
    {
        Category = category;
        Id = id;
    }

    public immutable int CompareTo(immutable(Record)* other)
    {
        if (Category < other.Category) return -1;
        if (Category > other.Category) return 1;
        return 0;
    }
}

int Id(Record value) => value.Id;

int main()
{
    Record[3] input = [new Record(2, 1), new Record(1, 2), new Record(1, 3)];
    var query = new Span<Record>(&input[0], 3).AsEnumerable()
        .Order().ThenByDescending(Id);
    int[3] expected = [3, 2, 1];
    nuint index = 0;
    foreach (Record value in query)
    {
        if (value.Id != expected[index]) return 1;
        index++;
    }
    if (index != 3) return 2;
    return 0;
}
```

As with [key-based ordering](QUERY_ORDER_BY.md), secondary selectors take values
by copy. Value ordering without secondary selectors compares the buffered
values directly, so it also supports noncopyable elements supplied as fresh
owning results by the source.

## Buffering

The first `MoveNext()` collects and sorts the complete input. Even
`Order().Take(1)` needs the full source, while `Take(0)` avoids materialization.
An infinite source cannot yield an ordered result.

Each enumeration builds a separate buffer from the source's current contents.
FileStream sources continue from their current position. Sorting uses O(n) element
storage plus index scratch and O(n log n) comparisons.

## Reading results

`Current` returns `ref immutable(T)` into the iterator's buffer. The reference
must stay within that iterator's lifetime and must not outlive completion of
`OrderDescending` or a secondary-ordering chain. Reading it by value invokes normal
copying. Noncopyable values need borrowed access and a source that supplies
fresh owning values during materialization.

Element types must fit `Vector<T>`. Project an outer readonly or immutable
scalar view to an ordinary value before buffering, as with
[Reverse](QUERY_REVERSE.md).

A materialization or comparison failure finishes the iterator. Later moves
return false. A `Current` copy failure leaves the buffered position available
for another read. `Order` keeps its buffer until iterator destruction.
`OrderDescending` and secondary-ordering chains destroy buffered elements when
iteration completes. They retain empty buffer capacity until destruction.

See [query ownership](QUERIES.md#ownership) for source lifetimes. Comparers must
keep a consistent order and borrow their arguments only during each call.

For `Order`, selective builds load `Queries/OrderSource.gf`,
`Queries/enumerable_order.gf`, `Queries/enumerable_to_vector.gf`, and StableSort
dependencies. `OrderDescending` loads `Queries/OrderValueKey.gf`,
`Queries/OrderBySource.gf`, and `Queries/enumerable_order_descending.gf`.
Default comparisons require `Collections/Comparer.gf`.

Secondary ordering after `Order` also loads `Queries/ThenOrderKey.gf`,
`Queries/OrderValueKey.gf`, `Queries/OrderBySource.gf`, and
`Queries/enumerable_order_then_by.gf`. Subsequent levels use
`Queries/enumerable_then_by.gf`.
