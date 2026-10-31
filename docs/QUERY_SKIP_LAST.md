# Omit a query's trailing values

`SkipLast(count)` forwards values in order while withholding the last at most
`count` values. It can stream an infinite source after an initial lookahead.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[5] data = [1, 2, 3, 4, 5];
    Span<int> values = new(data, 5);
    int expected = 1;
    foreach (int value in values.AsEnumerable().SkipLast(2))
    {
        if (value != expected) return 1;
        expected++;
    }
    return expected - 4;
}
```

## Input consumption

With a positive count, the first output needs `count + 1` input values.
Each later output needs one more. `SkipLast(n).Take(k)` consumes at most
`k + n` values for positive `k`, or the complete input if it ends first.

The buffer grows as input arrives. Storage is O(min(input length, count + 1)),
including the current output. A huge count on short input stores only those
observed values. `SkipLast(0)` forwards the source directly. A downstream
`Take(0)` constructs the upstream iterator but never fills the lookahead.

## Buffered values

`Current` copies the buffered front on each read. The next move releases that
front, and EOF releases the withheld tail. Values must support copying from
the buffer, including when the runtime count is zero. Project qualified scalar
views to ordinary values when needed for storage.

Lookahead advances a consumable source beyond its last yielded value. The
buffered values disappear when that iterator is destroyed. Another enumeration
continues from the source's new position. See [query ownership](QUERIES.md#ownership).

An input, copy, or allocation failure finishes the iterator. A buffered
`Current` copy failure leaves its position available for another read.

Sources: `Queries/enumerable_skip_last.gf`, `Queries/SkipLastSource.gf`, and
Deque dependencies.
