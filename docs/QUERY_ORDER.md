# Order a query

`Order()` uses `IComparable<T>` to yield a stable ascending order.
`Order(compare)` takes `int(immutable(T)*, immutable(T)*)*` for another order.
Equal-priority values keep their original encounter order.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int Descending(immutable(int)* first, immutable(int)* second)
{
    if (*first > *second) return -1;
    if (*first < *second) return 1;
    return 0;
}

int main()
{
    int[4] data = [3, 1, 2, 1];
    Span<int> values = new(&data[0], 4);
    var ascending = values.AsEnumerable().Order();
    var descending = values.AsEnumerable().Order(&Descending);
    if (ascending.First() != 1 || descending.First() != 3) return 1;
    if (data[0] != 3 || data[1] != 1) return 2;
    return 0;
}
```

## Buffering

The first `MoveNext()` collects and sorts the complete input. Even
`Order().Take(1)` needs the full source, while `Take(0)` avoids materialization.
An infinite source cannot yield an ordered result.

Each enumeration builds a separate buffer from the source's current contents.
FileStream sources continue from their current position. Sorting uses O(n) element
storage plus index scratch and O(n log n) comparisons.

## Reading results

`Current` returns `ref immutable(T)` into the iterator's buffer. The reference
must stay within that iterator's lifetime. Reading it by value invokes normal
copying. Noncopyable values need borrowed access and a source that supplies
fresh owning values during materialization.

Element types must fit `Vector<T>`. Project an outer readonly or immutable
scalar view to an ordinary value before buffering, as with
[Reverse](QUERY_REVERSE.md).

A materialization or comparison failure finishes the iterator. Later moves
return false. A `Current` copy failure leaves the buffered position available
for another read. The buffer stays alive until iterator destruction.

See [query ownership](QUERIES.md#ownership) for source lifetimes. Comparers must
keep a consistent order and borrow their arguments only during each call.

Sources: `Queries/OrderSource.gf`, `Queries/enumerable_order.gf`,
`Queries/enumerable_to_vector.gf`, and StableSort dependencies.
