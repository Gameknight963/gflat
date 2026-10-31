# Collect query results into a set

`ToHashSet()` consumes the query and returns an owning `HashSet<T>`:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

int main()
{
    int[5] input = [2, 1, 2, 3, 1];
    Span<int> values = new(input, 5);
    HashSet<int> distinct = values.AsEnumerable().ToHashSet();
    return (int)distinct.Count - 3;
}
```

Duplicates retain the first inserted value. Set iteration order is unspecified.
For lazy encounter-order results, use [Distinct](QUERY_DISTINCT.md).

Elements require `IHashable` and the matching `IEquatable<T>` contract. Equal
values need equal hashes, and stored values must keep their identity stable.
Borrowed owning elements are copied into the result. Fresh owning values from
a projection can transfer into it. A borrowed noncopyable owner cannot be
materialized this way.

Keep the source alive and stable during collection. If iteration, copying,
hashing, or allocation fails, the partial set is cleaned up. Consumed input and
callback effects remain visible. See [query ownership](QUERIES.md#ownership).

Source: `Queries/enumerable_to_hash_set.gf`, with [HashSet](HASH_COLLECTIONS.md)
and its dependencies.
