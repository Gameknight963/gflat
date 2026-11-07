# Select a prefix with its source indices

`TakeWhile(bool(T, nuint)* predicate)` yields elements while the predicate is
true. `SkipWhile(bool(T, nuint)* predicate)` discards that prefix and yields the
remaining elements, including the first element whose predicate is false.
Indices start at zero in the sequence passed to the operator.

```csharp
using Standard::Collections;
using Standard::Queries;

int main()
{
    Vector<int> values = new();
    values.Add(10);
    values.Add(20);
    values.Add(30);
    int sum = 0;
    foreach (int value in values.AsEnumerable().SkipWhile((int item, nuint index) => index < 1))
        sum += value;
    if (sum != 50) return 1;
    int count = 0;
    foreach (int value in values.AsEnumerable().TakeWhile((int item, nuint index) => index < 2))
        count++;
    return count - 2;
}
```

Both operators are deferred. Each enumerator starts its own index at zero.
Take stops immediately after the first false predicate result, without reading
another element. Skip stops calling the predicate at that boundary and yields the remaining
elements unchanged. An earlier filter changes the sequence
being indexed, so indices describe the filter's output.

The adapters allocate no internal storage. Predicates receive values using the
same copy/move semantics as the other query operators. Source-view copies,
element copies, and callbacks can allocate or throw. Exceptions propagate, and iterator-owned
resources are released when the iterator leaves scope. Current reads are not
cached. Keep borrowed sources alive and structurally unchanged while enumerating.

An index that would exceed `nuint` throws before invoking the predicate. Skip
stops tracking indices after its predicate becomes false.

Selective builds include `Queries/IndexedTakeWhileSource.gf`,
`Queries/IndexedSkipWhileSource.gf`, and `Queries/enumerable_indexed_while.gf`,
plus the source sequence's dependencies. Freestanding forms take the sequence
first: `TakeWhile(source, predicate)` and `SkipWhile(source, predicate)`.
