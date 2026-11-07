# Query set operations

`Union`, `Except`, and `Intersect` combine two queries using each element's
`IHashable` and `IEquatable<T>` implementations. Each result contains distinct
values in encounter order.

| Operation | Result order |
| --- | --- |
| `first.Union(second)` | First appearances in `first`, followed by new values from `second` |
| `first.Except(second)` | First appearances in `first` that are absent from `second` |
| `first.Intersect(second)` | First appearances in `first` that also occur in `second` |

For equal values with different payloads, the first matching element in the
result's encounter order supplies the payload. These methods also have
freestanding forms such as `Union(first, second)`.

```csharp
using Standard::Collections;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[5] firstValues = [3, 1, 3, 2, 1];
    int[4] secondValues = [2, 4, 2, 1];
    Span<int> first = new(firstValues, 5);
    Span<int> second = new(secondValues, 4);

    Vector<int> combined = first.AsEnumerable().Union(second.AsEnumerable()).ToVector();
    Vector<int> difference = first.AsEnumerable().Except(second.AsEnumerable()).ToVector();
    Vector<int> common = first.AsEnumerable().Intersect(second.AsEnumerable()).ToVector();
    if (combined.Count != 4 || combined[0] != 3 || combined[1] != 1
        || combined[2] != 2 || combined[3] != 4) return 1;
    if (difference.Count != 1 || difference[0] != 3) return 2;
    if (common.Count != 2 || common[0] != 1 || common[1] != 2) return 3;
    return 0;
}
```

## Evaluation and storage

Constructing a query over borrowed collection views does not consume elements
or allocate a set. `GetEnumerator()` creates both input iterators. Each
enumerator owns its own hash set, so later enumerations start fresh.

- `Union` reads the first sequence before advancing the second. Its set grows
  with the distinct values seen in both inputs
- `Except` and `Intersect` read the complete second sequence on the first
  `MoveNext()` call, before advancing the first. The second input must be finite,
  including when the first is empty or a later `Take` limits the result
- `Except` retains the second input's distinct values and each newly yielded value
- `Intersect` removes a matching value from its set when yielding it

Keep the borrowed input owners alive and their storage stable while using the
query. Set entries own copies of values when the element type requires copying.
Hashing and equality of retained values must stay stable. Input iterators and
set entries are released when the result enumerator leaves scope, including
early `break`, return, and exception unwinding.

Allocation, copying, iterator operations, hashing, and equality can throw.
Exceptions propagate without replacement. An exception from `MoveNext()` ends
that enumerator. `Current` is unavailable before the first successful advance,
after completion, or after a failed advance.
