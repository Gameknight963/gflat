# Pair sequence items with their positions

`source.Index()` lazily produces `Indexed<T>` values with public `Index` and
`Item` fields. Indices start at zero for each enumeration and use `nuint`,
matching the existing indexed `Select` and `Where` callbacks.

```csharp
using Standard::Collections;
using Standard::Memory;
using Standard::Queries;

bool Even(Indexed<int> value) => value.Item % 2 == 0;

int main()
{
    int[4] values = [7, 4, 9, 2];
    Span<int> input = new(values, 4);
    Vector<Indexed<int>> matches = input.AsEnumerable().Index().Where(Even).ToVector();
    if (matches.Count != 2) return 1;
    if (matches[0].Index != 1 || matches[0].Item != 4) return 2;
    if (matches[1].Index != 3 || matches[1].Item != 2) return 3;
    return 0;
}
```

The freestanding form is `Index(source)`.

## Placement and evaluation

The position describes the sequence reaching `Index`. Placing it before
`Where` or `Skip` retains the original positions. Placing it afterward starts
numbering the filtered or sliced sequence from zero.

Constructing the query does not open an iterator or read elements.
`GetEnumerator()` opens the source iterator. Each successful `MoveNext()`
advances that iterator and updates the position, without reading `Current`.
Each read of the indexed `Current` reads the source's `Current` once and
constructs an `Indexed<T>` value. It does not cache items. A terminal operation
such as `Count()` can therefore count positions without copying item values.

The adaptor uses inline state and does not allocate its own buffer. Input
iterators and copying item values can still allocate or throw.

## Ownership and errors

The query retains its input's borrowing rules. Keep borrowed source storage
alive and stable during enumeration. Each indexed result follows `T`'s normal
value semantics. An owning `String` item is copied into the result. Pointer
items remain borrowed pointers, and indexing does not extend their pointees'
lifetimes.

`Current` is unavailable before the first successful advance, after completion,
or after a failed `MoveNext()`. Exhaustion or an exception from `MoveNext()`
ends that enumerator permanently. Errors propagate, and the input iterator is
destroyed when the result iterator leaves scope. An error while obtaining or
copying `Current` leaves the position available for another read.

If another element would need an index larger than `nuint` maximum,
`MoveNext()` throws `Exception("Sequence index overflow")` after advancing the
source to that element, before reading its value. The index never wraps.

Selective builds include `Queries/Indexed.gf`, `Queries/IndexSource.gf`, and
`Queries/enumerable_index.gf` alongside the source query's dependencies.
