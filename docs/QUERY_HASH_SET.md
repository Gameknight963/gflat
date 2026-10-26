# Materializing query results into a set

Include `core/Queries/enumerable_to_hash_set.gf` alongside the usual query and
`HashSet` sources to use `query.ToHashSet()`. This terminal enumerates the query
immediately and returns a new owning `HashSet<T>`. Repeated calls enumerate
again and produce independent sets. Results have unspecified iteration order.

[`Distinct()`](QUERY_DISTINCT.md) provides lazy encounter-order enumeration with
its hash storage owned by each iterator.

```csharp
using std; using std::Memory; using std::Queries; using std::Collections;

int main()
{
    int[5] input = [2, 1, 2, 3, 1];
    Span<int> values = new(input, 5);
    HashSet<int> distinct = values.AsEnumerable().ToHashSet();
    return (int)distinct.Count - 3;
}
```

Requirements match `HashSet<T>`: elements implement `IHashable` and
`IEquatable<T>`. Equal values must have equal hashes, and stored values must keep
their hash/equality stable. No qualifier is removed to satisfy these requirements.
A primitive or owning `String` query can be materialized directly. Other element
types follow their existing interface implementations and ordinary value rules.

Reading an element from borrowed storage performs its normal value copy; a
borrowed noncopyable owning element cannot be materialized this way. Fresh values
returned by an iterator or projection transfer normally. A `String` result owns
independent buffers, so later source mutation or destruction does not change it.
Duplicates retain the first inserted value; unused duplicate arguments are
destroyed normally. The source is not structurally changed by this terminal.

The iterator and partial set are ordinary scoped owners. If iterator construction,
advancement, reading `Current`, projection, copying, hashing, equality, or allocation
throws, the partial result, active iterator, and temporary arguments are cleaned
up before the original failure propagates. No partial set is returned. External
side effects of callbacks are not rolled back. Existing source owners remain
owned by their source, subject to ordinary callback behavior.

Keep borrowed storage alive and structurally unchanged while materializing it.
Hash/equality/copy callbacks must not mutate the set under construction or source
through other aliases. Pointer values gain no ownership or interface support from
this method; the current hash interfaces do not supply built-in pointer adapters.
The method introduces no GC or runtime policy beyond the existing HashSet,
iterator, element, and exception behavior.
