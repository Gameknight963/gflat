# Span algorithms

Algorithms borrow a span and never copy or own the source collection.
Import `std` and call them as ordinary functions:

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
    Vector<int> values = new();
    values.Add(9); values.Add(2); values.Add(5);
    Sort<int>(values.AsSpan(), Compare);
    int key = 5;
    nuint index = LowerBound<int>(values.AsReadOnly(), &key, Compare);
    return (int)index - 1;
}
```

| Function | Behavior |
| --- | --- |
| `Sort<T>(Span<T>, compare)` | In-place ascending heapsort; O(n log n) comparisons and constant extra storage. Not stable. |
| `Reverse<T>(Span<T>)` | Reverse elements in place without invoking user code. |
| `LowerBound<T>(Span<readonly(T)>, key, compare)` | First index whose element compares greater than or equal to the borrowed key. |
| `UpperBound<T>(Span<readonly(T)>, key, compare)` | First index whose element compares greater than the borrowed key. |
| `IsSorted<T>(Span<readonly(T)>, compare)` | Test whether every adjacent pair is in ascending order; equal elements are allowed. |
| `SequenceEqual<T>(first, second, equal)` | Compare equal-length readonly spans element by element, stopping at the first difference. |
| `FindIndex<T>(Span<readonly(T)>, predicate)` | First matching index, or span length when no element matches. |
| `Partition<T>(Span<T>, predicate)` | Move matching elements into a prefix and return its length; relative order is not preserved. |

Both bounds return the span length if no such element exists, including zero
for an empty span. Input must already be sorted under the same comparator.
Their result identifies the insertion position, whether or not the key exists.
For duplicate keys, `[LowerBound, UpperBound)` describes the matching range.

Comparators borrow two elements through readonly pointers and return a negative,
zero, or positive `int`. They must provide a consistent ordering and must not
mutate the elements or collection through other aliases.

For types implementing `IComparable<T>`, `Comparer<T>::Compare` supplies the
comparison callback, for example `Sort<int>(span, Comparer<int>::Compare)`.
`EqualityComparer<T>::Equals` similarly adapts `IEquatable<T>` for
`SequenceEqual`. These are static bridges and allocate no comparer objects.
Explicit custom callbacks remain available for other orderings or equality rules.

Sorting and reversing relocate values rather than copying them. Owning values
without copy constructors are supported; every value remains responsible for
its resources in its final slot. These operations do not allocate or free
storage and do not run element destructors. A comparator may allocate or throw.
If it throws during sorting, ordering may be partial but every element remains
alive and owned by the source collection.

`Partition` has the same ownership guarantee if its predicate throws. The
predicate borrows an element through `readonly(T)*` and runs once per element
unless an earlier invocation throws. `FindIndex` stops at the first match;
`IsSorted` stops at the first out-of-order adjacent pair. Empty spans are sorted
and compare equal to other empty spans without invoking callbacks.
