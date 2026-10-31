# Reverse a query

`Reverse()` yields the source's values in reverse order using a separate buffer.
Use the span `Reverse` algorithm to reorder existing mutable storage in place.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[4] values = [1, 2, 3, 4];
    Span<int> source = new(values, 4);
    var backwards = source.AsEnumerable().Reverse();
    int expected = 4;
    foreach (int value in backwards)
    {
        if (value != expected) return 1;
        expected--;
    }
    if (expected != 0 || values[0] != 1 || values[3] != 4) return 2;
    return 0;
}
```

## Buffering

The first `MoveNext()` collects the complete input. Even `Reverse().Take(1)`
requires the full source. `Take(0)` avoids materialization, and an infinite
source cannot yield a first result.

Each iterator builds its own `Vector<T>`, using O(n) storage and linear
materialization work. Source changes before buffering are visible. Later
changes leave that iterator's copied values intact.

Outer readonly or immutable element types need a projection to a writable
stored type before buffering:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[3] values = [1, 2, 3];
    Span<int> source = new(values, 3);
    var backwards = source.AsReadOnly().AsEnumerable()
        .Select((readonly(int) value) => (int)value).Reverse();
    int expected = 3;
    foreach (int value in backwards)
    {
        if (value != expected) return 1;
        expected--;
    }
    return expected;
}
```

## Reading results

`Current` returns a value copied from the buffered position. Repeated reads can
copy an owner again. A copy failure leaves that position available for retry.
A materialization failure permanently finishes the iterator.

The buffer remains alive until iterator destruction, including after EOF.
Keep borrowed source storage valid while materializing. A file-line source
continues from its current position on each enumeration. See
[query ownership](QUERIES.md#ownership).

Sources: `Queries/enumerable_reverse.gf`, `Queries/ReverseSource.gf`, and
`Queries/enumerable_to_vector.gf`, with Vector dependencies.
