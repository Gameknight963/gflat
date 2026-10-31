# Project values with their encounter index

Indexed `Select` passes a zero-based `nuint` index to a `U(T, nuint)*` selector:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

int main()
{
    int[3] data = [8, 4, 9];
    Span<int> values = new(data, 3);
    Vector<int> numbered = values.AsEnumerable()
        .Select((int value, nuint index) => value + (int)index).ToVector();
    if (numbered.Count != 3) return 1;
    if (numbered[0] != 8 || numbered[1] != 5 || numbered[2] != 11) return 2;
    return 0;
}
```

Every successful move consumes an index, even if its `Current` is skipped.
Reading `Current` invokes the selector each time with the current index.
`Count()` and `Any()` can advance without invoking it.

A preceding filter changes which encounters are indexed. A following filter
keeps the assigned indices.

## Failures and callback values

A selector or input-getter failure leaves the current index available for
another read. If upstream produces a value after index `nuint` maximum, the
adapter throws `Sequence index overflow` and finishes permanently. The extra
upstream value has already been consumed.

The selector takes `T` by value. An owning borrowed value is copied, and the
result must not borrow storage owned only by that temporary argument. Use
named functions for owning arguments such as String. Callbacks use the
language's noncapturing function-pointer rules.

See [query ownership](QUERIES.md#ownership) for source lifetimes.
Sources: `Queries/IndexedSelectSource.gf` and
`Queries/enumerable_indexed_select.gf`.
