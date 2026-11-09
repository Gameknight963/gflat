# Merge sorted spans

`MergeSorted<T>(first, second)` copies two sorted spans into a new owning
`Vector<T>`. The result keeps the order of equivalent elements within each input.
Equivalent elements from `first` precede those from `second`.

The two-argument overload uses `IComparable<T>`. Pass a third argument of type
`int(immutable(T)*, immutable(T)*)*` to supply an ordering. Both inputs must already
be sorted by that ordering. The function does not check this precondition.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[3] left = [1, 3, 5];
    int[3] right = [2, 3, 4];
    Span<int> first = new(left, 3);
    Span<int> second = new(right, 3);
    Vector<int> merged = MergeSorted<int>(first.AsImmutable(), second.AsImmutable());
    if (merged.Count != 6 || merged[0] != 1 || merged[5] != 5) return 1;
    return 0;
}
```

Element copying must be supported. The input spans may overlap. The result owns
its copies and can outlive the inputs. An empty pair produces an empty vector
without allocating. If either input is empty, the other is copied without calling
the comparator.

Length overflow is rejected before reserving the result buffer or comparing
elements. Allocation, comparison, or element-copy failure releases the partial
result. Assignment from the call replaces the destination only after the merge
succeeds.
