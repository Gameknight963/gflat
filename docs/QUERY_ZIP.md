# Pairwise queries

`left.Zip(right, selector)` combines corresponding elements and stops when
either input ends:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int[3] numbers = [1, 2, 3];
int[2] offsets = [10, 20];
Span<int> first = new(numbers, 3);
Span<int> second = new(offsets, 2);
var sums = first.AsEnumerable().Zip(second.AsEnumerable(),
    (int value, int offset) => value + offset);
// Enumerating sums produces 11, 22.
```

The two inputs can use different element, source, and iterator types. The
selector follows noncapturing function-pointer rules. Named functions can
return owning values such as String.

## Evaluation

Iterator construction creates both input iterators, left first. Each move
advances left, then right if left succeeded. If right ends first, left has
already consumed one unmatched element. Its input callbacks or errors may
therefore occur before the shorter length is known.

`Current` calls the selector for the current pair on every read. `Count()` and
`Any()` can advance without calling it. A by-value String argument is copied
when passed to the selector.

After a failed move, discard the iterator. Prior input advancement and callback
effects remain visible. Both input iterators stay owned until its destruction.
Keep backing source storage alive and stable, following [query ownership](QUERIES.md#ownership).

Sources: `Queries/enumerable_zip.gf` and `Queries/ZipSource.gf`.
