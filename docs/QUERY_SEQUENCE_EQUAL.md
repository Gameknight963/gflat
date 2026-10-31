# Compare two queries

`SequenceEqual(other)` returns true when corresponding elements are equal and
both sequences end together. The queries must share the same element type,
but can use different source and iterator types.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

bool SameParity(immutable(int)* left, immutable(int)* right)
    => *left % 2 == *right % 2;
int main()
{
    int[3] first = [1, 2, 3];
    int[3] second = [5, 6, 7];
    Span<int> left = new(first, 3);
    Span<int> right = new(second, 3);
    if (!left.AsEnumerable().SequenceEqual(left.AsEnumerable())) return 1;
    if (left.AsEnumerable().SequenceEqual(right.AsEnumerable())) return 2;
    if (!left.AsEnumerable().SequenceEqual(right.AsEnumerable(), SameParity)) return 3;
    if (!left.AsImmutable().AsEnumerable().SequenceEqual(right.AsImmutable().AsEnumerable(), SameParity)) return 4;
    return 0;
}
```

Default equality requires the matching `IEquatable<T>` contract.
`SequenceEqual(other, equal)` accepts
`bool(immutable(T)*, immutable(T)*)*` for another relation or qualified views.
The callback receives the left element first.

## Evaluation and lifetime

Iterators are constructed left first. Each comparison round advances left,
then right, including a right move after left reports EOF. If both advance,
their `Current` values are read left first and compared. A mismatched pair or
length ends the operation immediately.

Even identical wrappers enumerate independently. Take care when both borrow
the same consumable source, such as a file cursor.

Element values are materialized in temporaries. A comparer must not retain
their addresses or invalidate either source. For contiguous spans, the span
SequenceEqual algorithm can compare borrowed elements directly and reject
unequal lengths immediately.

See [query ownership](QUERIES.md#ownership).
Source: `Queries/enumerable_sequence_equal.gf`, plus EqualityComparer for
default equality.
