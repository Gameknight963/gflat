# Enumeration and queries

Import `Standard::Queries` and call `AsEnumerable()` on a collection or view to
build a lazy query:

```csharp
using Standard::Text;
using Standard;
using Standard::Queries;
using Standard::Collections;

int main()
{
    Vector<int> numbers = new Vector<int>();
    for (int i = 1; i <= 10; i++) numbers.Add(i);

    Vector<int>* borrowed = &numbers;
    var query = borrowed.AsEnumerable()
        .Where((int n) => n % 2 == 0)
        .Select((int n) => n * n)
        .Take(3);

    foreach (int number in query)
    {
        String line = s$"{number}";
        Console::WriteLine(ref line);
    }
    return 0;
}
```

The query borrows the vector's storage. Keep its owner alive and avoid growth,
removal, or clearing while using the query. Editing existing elements is visible
on later reads.

## Operations

- Compute numeric and projected means with [Average](../../../docs/QUERY_AVERAGE.md)

- Filter and project with `Where` and `Select`
- Flatten projected sequences with [SelectMany](../../../docs/QUERY_SELECT_MANY.md)
- Join sequences by key with [Join](../../../docs/QUERY_JOIN.md)
- Retain each outer value and its shared matches with [GroupJoin](../../../docs/QUERY_GROUP_JOIN.md)
- Group values in first-key order with [GroupBy](../../../docs/QUERY_GROUP_BY.md)
- Retain positions with [Index](../../../docs/QUERY_INDEX.md)
- Slice with `Skip`, `Take`, `SkipWhile`, and `TakeWhile`
- Combine with `Zip`, `Concat`, `Append`, and `Prepend`
- Combine distinct values with [Union, Except, and Intersect](../../../docs/QUERY_SET_OPERATIONS.md)
- Combine records by key with [UnionBy, ExceptBy, and IntersectBy](../../../docs/QUERY_SET_OPERATIONS_BY.md)
- Search with `Any`, `All`, `Contains`, `First`, `Last`, and `ElementAt`
- Summarize with `Count`, [Sum](../../../docs/QUERY_SUM.md), `Aggregate`, `Min`, and `Max`
- Summarize groups with `CountBy` and [AggregateBy](../../../docs/QUERY_AGGREGATE_BY.md)
- Order with `Order`, `Reverse`, `TakeLast`, and `SkipLast`
- Collect results with `ToVector`, `ToHashSet`, `ToHashMap`, and `ToLookup`

See the [operation reference](../../../docs/QUERIES.md#operations) for overloads,
empty-sequence behavior, and callback rules.

Hash and sorted sets also provide `AsEnumerable()`, yielding borrowed immutable
element pointers. See [set queries](../../../docs/SET_QUERIES.md).

## Ownership

Query views borrow collection storage. Iterators clean up their owned values on
completion, `break`, return, and exceptions. Buffered operators such as `Order`
and `Reverse` own temporary storage. `ToVector()` returns an owning collection.
Raw pointer elements retain their ordinary borrowed-pointer semantics.

See [ownership](../../../docs/QUERIES.md#ownership) and
[allocation behavior](../../../docs/QUERIES.md#allocation-and-callbacks).

## Custom sequences and optional interfaces

`foreach` accepts a `GetEnumerator()` / `MoveNext()` / `Current` pattern.
`IEnumerable<T, Iterator>` and `IEnumerator<T>` express those contracts when an
interface is useful. The concrete iterator type supports value iteration.

See [custom sequences](../../../docs/QUERIES.md#custom-sequences-and-optional-interfaces).

## Select an element by its key

`MinBy` and `MaxBy` return the element with the least or greatest selected key.
See [keyed extrema](../../../docs/QUERIES.md#select-an-element-by-its-key).

### Empty-safe keyed extrema

`TryMinBy` and `TryMaxBy` leave their output unchanged for empty input.
See [the complete example](../../../docs/QUERIES.md#empty-safe-keyed-extrema).
