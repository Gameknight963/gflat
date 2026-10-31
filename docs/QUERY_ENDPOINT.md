# Add a query endpoint

`Prepend(value)` yields one captured value before the source. `Append(value)`
yields it afterward. An empty source still yields the endpoint.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[2] data = [1, 2];
    Span<int> values = new(data, 2);
    var query = values.AsEnumerable().Prepend(0).Append(3);
    int expected = 0;
    foreach (int value in query)
    {
        if (value != expected) return 1;
        expected++;
    }
    return expected - 4;
}
```

Use [DefaultIfEmpty](QUERY_DEFAULT_IF_EMPTY.md) for a fallback that appears only
when the source is empty. An infinite source never reaches an appended value.

## Capturing values

The query stores the endpoint, and each iterator copies it. `Append(move value)`
and `Prepend(move value)` transfer an owner into the query, but the element type
must support the per-iterator copy. Pointer endpoints retain their target's
ordinary lifetime requirements.

The endpoint has the sequence's existing `T` type. Project an outer readonly or
immutable element to an ordinary value before capture when needed, for example
`.Select((readonly(int) value) => (int)value).Append(3)`.

## Evaluation

Iterator construction also constructs the upstream iterator, even for
`Prepend(value).Take(1)`. Its first move yields the endpoint without advancing
upstream. Append yields the endpoint after the source ends.

`Current` copies the stored endpoint on each read. Source positions delegate to
the upstream getter, including repeated selector evaluation. A failed move
permanently finishes that iterator. A failed `Current` copy leaves its position
available for another read.

Source lifetime rules are covered in [query ownership](QUERIES.md#ownership).
Sources: `Queries/EndpointSource.gf` and `Queries/enumerable_endpoints.gf`.
