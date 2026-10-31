# Concatenating queries

`first.Concat(second)` yields the first query followed by the second. Both must
have the same element type, but their source and iterator types may differ.

```cpp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[2] prefix = [1, 2];
    Span<int> first = new(prefix, 2);
    IntRange second = new(3, 3);
    int sum = 0;
    foreach (int value in first.AsEnumerable().Concat(second.AsEnumerable()))
        sum += value;
    if (sum != 15) return 1;
    return 0;
}
```

## Evaluation

`GetEnumerator()` constructs both input iterators, left first. `MoveNext()`
advances the left until it ends, then the right. Both iterators remain alive
until the Concat iterator is destroyed.

`Current` reads the active input each time. For example, a preceding `Select`
can run again on repeated reads. Results follow ordinary value copying rules.

An exception from `MoveNext()` finishes the wrapper permanently. Later moves
return false and `Current` is invalid. A failed `Current` read leaves its
position unchanged.

## Sources and lifetime

Concat stores copies of its source views. Keep borrowed backing owners alive
and stable. Re-enumeration follows each source's behavior: spans restart, while
file-line views continue at the shared file position. See [query ownership](QUERIES.md#ownership).

Selective builds need `Queries/enumerable_concat.gf` and `Queries/ConcatSource.gf`
with the ordinary query and input dependencies.
