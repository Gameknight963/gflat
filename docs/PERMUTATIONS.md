# Advancing lexicographic arrangements

`NextPermutation<T>(Span<T> values, compare)` rearranges values into their next
lexicographic ordering under the comparator. `PreviousPermutation` chooses the
previous ordering. Each returns true when it advances. At the last or first
ordering respectively, it wraps to the opposite endpoint and returns false.

These operations are useful for bounded arrangement searches: enumerate small
assignment or route candidates without allocating a collection for every order.
The number of arrangements can still grow factorially; the caller controls the
input size and stopping policy.

```csharp
using std;
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

Begin with the first ordering to enumerate every distinct comparator-key
arrangement using `NextPermutation`, or the last ordering with
`PreviousPermutation`. Arbitrary starting arrangements are also accepted.
Duplicate keys are handled without treating comparator-equivalent values as
different key arrangements. Equal values do not have a stable identity order;
wrapping can rearrange equivalent owners too.

Empty and singleton spans return false without comparing or accessing element
storage. For larger spans, false still means the values were wrapped; it is not
a no-op or a failed-capacity result. A custom reversed comparator reverses the
meaning of first and last. Only the sign of its result matters, including
non-unit magnitudes or the minimum `int`; no result is negated.

Each call uses O(n) comparisons and relocation work, constant extra storage, and
no heap scratch allocation. It borrows mutable elements and invokes no element
copy constructor or destructor. Noncopyable owners are supported through the
existing public `Swap` and `Reverse` relocation operations. Collector hooks and
comparator effects retain their ordinary behavior. Protected element storage
is not made writable by these algorithms.

Comparators borrow `immutable(T)*` values. They must provide a consistent
ordering, must not mutate elements or the span through aliases, and must not
retain element pointers across calls. All comparisons finish before the first
swap or reversal, so a comparator exception leaves the original arrangement
unchanged. External callback side effects are not rolled back. Normal
collector-hook effects still apply; no extra failure policy is introduced.

Pointers to element slots continue to refer to those slots, which may now hold
different owners. Pointers into separately owned allocations keep their address
and follow the transferred owner's lifetime. Raw aliases inside values are not
retargeted. Keep the source storage alive and obey the usual in-place algorithm
borrowing rules.

Include optional `Collections/Permutations.gf` and `Collections/Algorithms.gf`,
Span and their normal dependencies. `Collections/DefaultOrdering.gf` adds
one-argument `NextPermutation(values)` and `PreviousPermutation(values)` for
`T : IComparable<T>`, using `Comparer<T>::Compare`; include `Collections/Comparer.gf` and
`IComparable.gf` for those conveniences. Explicit callbacks remain available
without that interface. There is no permutation-sequence wrapper.
