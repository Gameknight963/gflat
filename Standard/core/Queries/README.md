# Enumeration and queries

Import `Standard::Queries` and call `AsEnumerable()` on a collection or view to
build a lazy query:

```csharp
using Standard::Text;
using Standard; using Standard::Queries; using Standard::Collections;

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

The query borrows the vector's storage. Keep the vector alive and avoid growth,
removal, clearing, or movement while using the query. Editing existing elements
is visible on later reads.

## Operations

- Filter and project with `Where` and `Select`
- Slice with `Skip`, `Take`, `SkipWhile`, and `TakeWhile`
- Combine with `Zip`, `Concat`, `Append`, and `Prepend`
- Search with `Any`, `All`, `Contains`, `First`, `Last`, and `ElementAt`
- Summarize with `Count`, `Sum`, `Aggregate`, `Min`, and `Max`
- Order with `Order`, `Reverse`, `TakeLast`, and `SkipLast`
- Collect results with `ToVector`, `ToHashSet`, `ToHashMap`, and `ToLookup`

See the [operation reference](../../../docs/QUERIES.md#operations) for overloads,
empty-sequence behavior, and callback rules.

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
`TryMinBy` and `TryMaxBy` return false for empty input.
See [keyed extrema](../../../docs/QUERIES.md#select-an-element-by-its-key).

### Empty-safe keyed extrema

`TryMinBy` and `TryMaxBy` leave their output unchanged for empty input.
See [the complete example](../../../docs/QUERIES.md#empty-safe-keyed-extrema).
