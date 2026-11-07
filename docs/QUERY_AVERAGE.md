# Sequence averages

Load `Standard/core/Queries/enumerable_average.gf` to use `Average()` on
sequences of `int`, `long`, `float`, or `double`. Selector overloads accept a
function returning any of those types. Both forms also support freestanding
`Average(sequence, ...)` calls.

```csharp
using Standard::Memory;
using Standard::Queries;

int LengthSquared(int value) => value * value;

int main()
{
    int[3] values = [2, 4, 6];
    Span<int> span = new(&values[0], 3);
    if (span.AsEnumerable().Average() != 4.0) return 1;
    if (Average(span.AsEnumerable(), LengthSquared) != 56.0 / 3.0) return 2;
    return 0;
}
```

Integer averages return `double` and check every prefix of a signed 64-bit
sum. An overflowing prefix throws even if later values would bring the sum
back into range. Floating averages accumulate in `double`; a `float` sequence
or selector returns `float` after the final division. NaN and infinities
follow floating arithmetic. Accumulation follows enumeration order.

Empty sequences throw. The iterator is destroyed on success or failure.
Each visited `Current` and each selector are evaluated once. A selector takes
its input by value, following ordinary element copy/move semantics.
Iterator and selector failures propagate without visiting later elements.
The average operation does not buffer the sequence or allocate its own storage.
The sequence count is checked against the maximum signed 64-bit count.
