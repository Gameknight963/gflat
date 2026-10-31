# Default value for an empty query

`DefaultIfEmpty(value)` yields the source, or the supplied value once if the
source is empty:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    Span<int> empty = new();
    var query = empty.AsEnumerable().DefaultIfEmpty(42);
    int count = 0;
    foreach (int value in query)
    {
        if (value != 42) return 1;
        count++;
    }
    return count - 1;
}
```

Put it after a filter to provide a fallback for an empty filtered result.
A filter placed after it can remove that fallback.

## Capturing the fallback

The query stores the value and copies it into each new iterator, even if the
source later proves nonempty. Named owners are copied when captured.
`DefaultIfEmpty(move value)` transfers the owner into the query, but its type
must still support the per-iterator copy.

The fallback has the sequence's existing element type. Outer `readonly` or
`immutable` elements can require an explicit projection to an ordinary value
before capture, as with [Append](QUERY_ENDPOINT.md).

## Iteration

The first move tests the source. An empty source selects the fallback once.
After a nonempty source ends, iteration finishes normally.

`Current` copies the captured fallback or reads the source's `Current` each
time. Repeated source reads can repeat an upstream selector. A `MoveNext()`
exception permanently finishes that iterator. A `Current` error leaves its
position available for another read.

Each iterator owns its fallback copy until destruction. Borrowed source views
still follow the [query lifetime rules](QUERIES.md#ownership).

Sources: `Queries/DefaultIfEmptySource.gf` and
`Queries/enumerable_default_if_empty.gf`.
