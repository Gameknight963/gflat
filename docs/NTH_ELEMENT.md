# Selecting an element by rank

`Standard::Collections::NthElement(values, nth, compare)` puts the value of sorted
rank nth at that index. Earlier values compare no greater, and later values no
less. Each side can remain unordered.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

int Compare(readonly(int)* left, readonly(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}
int main()
{
    int[7] data = [9, 1, 8, 2, 7, 3, 6];
    Span<int> values = new(data, 7);
    NthElement<int>(values, 3, Compare);
    if (values[3] != 6) return 1;
    for (nuint i = 0; i < 3; i++) if (values[i] > values[3]) return 2;
    for (nuint i = 4; i < values.Length; i++) if (values[i] < values[3]) return 3;
    return 0;
}
```

Pass rank k to select the smallest k values into the prefix. For a median,
choose a rank according to the application's even-length policy.

| Rank | Effect |
| --- | --- |
| Below Length | Selects the element |
| Equal to Length | Leaves the span unchanged |
| Above Length | Throws `Exception("Selection index out of range", 3)` |

The comparator borrows immutable element pointers and must define a consistent
order without changing the span through another alias. A comparator exception
can leave a partially rearranged permutation. Element slots remain in place,
while the owners stored there may move.

Noncopyable values are supported. The implementation uses partitioning with a
sorting fallback, giving O(n log n) worst-case comparisons and constant working
storage. Default `NthElement(values, nth)` uses IComparable through Comparer.

Sources: `core/Collections/nth_element.gf`, `span_algorithms.gf`, and
`default_ordering.gf` for default comparison.
