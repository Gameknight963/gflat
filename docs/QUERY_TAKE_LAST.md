# Take a query's final values

`TakeLast(count)` yields the final at most `count` values of a finite input,
keeping their original order:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[5] input = [1, 2, 3, 4, 5];
    Span<int> values = new(input, 5);
    int expected = 4;
    foreach (int value in values.AsEnumerable().TakeLast(2))
    {
        if (value != expected) return 1;
        expected++;
    }
    return expected - 6;
}
```

## Buffering

For a positive count, the first move consumes the full input and destroys its
upstream iterator before yielding. An infinite input cannot produce a tail.
`TakeLast(2).Take(1)` still reads everything.

`TakeLast(0)` skips upstream iterator construction and advancement. A
downstream `Take(0)` also avoids filling the tail buffer.

Each iterator owns a Deque that grows as values arrive, retaining at most
`count` elements. Storage is O(min(count, input length)), plus spare capacity.
Each candidate's `Current` is read once while filling that buffer.

## Results and lifetime

`Current` copies a value from the buffer on every read. Elements must support
that copy. Project qualified scalar views to ordinary stored values when
necessary. The tail remains alive until iterator destruction, including after
exhaustion.

Each enumeration consumes the source again. File-line views continue from
their shared current position. See [query ownership](QUERIES.md#ownership).

An input, copy, or allocation failure finishes the iterator and releases its
staged tail. A successful buffered position remains available after a
`Current` copy failure.

Sources: `Queries/enumerable_take_last.gf`, `Queries/TakeLastSource.gf`, and
Deque dependencies.
