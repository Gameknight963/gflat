# Advancing lexicographic arrangements

`Standard::Collections::NextPermutation(values, compare)` advances a mutable span
to its next comparer ordering. PreviousPermutation moves backward.

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
    int[3] data = [1, 1, 2];
    Span<int> values = new(data, 3);
    int arrangements = 1;
    while (NextPermutation<int>(values, Compare)) arrangements++;
    if (arrangements != 3 || data[0] != 1 || data[1] != 1 || data[2] != 2)
        return 1;
    if (PreviousPermutation<int>(values, Compare)) return 2;
    if (data[0] != 2 || data[1] != 1 || data[2] != 1) return 3;
    return 0;
}
```

Success returns true. At the endpoint, the span wraps to the opposite ordering
and returns false. Empty and singleton spans return false unchanged.

Start in the first ordering to enumerate all distinct arrangements with
NextPermutation, or the last ordering with PreviousPermutation. Comparer-equivalent
values share one key arrangement, while their identities can change order.

Comparators borrow immutable element pointers and must preserve a consistent
ordering. A comparator exception leaves the arrangement unchanged because
comparisons finish before movement. Slots can hold different owners after success,
so reacquire any value-specific borrows.

Each call takes O(n) time and constant working storage. Noncopyable values work.
The default overloads use IComparable through Comparer.

Sources: `core/Collections/span_permutations.gf`, `span_algorithms.gf`, and
`default_ordering.gf` for default comparison.
