# Reversing and sorting vectors

`Vector<T>.Reverse()` reverses the full vector. `Sort()` sorts it using
`IComparable<T>`. Both operations also accept a `start, count` range.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    values.Add(8);
    values.Add(3);
    values.Add(1);
    values.Add(6);
    values.Sort(1, 2);
    if (values[0] != 8 || values[1] != 1 || values[2] != 3 || values[3] != 6) return 1;
    values.Reverse();
    if (values[0] != 6 || values[3] != 8) return 2;
    values.Sort();
    if (values[0] != 1 || values[3] != 8) return 3;
    return 0;
}
```

Custom ordering uses `Sort(compare)` or `Sort(start, count, compare)` with a
borrowed `int(immutable(T)*, immutable(T)*)*` comparator. Negative means less,
zero means equivalent, and positive means greater. Default sorting requires
`IComparable<T>`. Reversal and custom sorting impose no comparison interface.

The operations rearrange elements in existing storage without allocating or
copying owners. Noncopyable elements are supported. Count, capacity, and storage
address stay unchanged. Existing views observe the reordered values. Pointers
to slots can refer to different values after reordering.

Ranges are validated before changing elements or invoking comparisons. An empty
range at `Count` is valid. Invalid ranges throw. Elements outside a valid range
are unchanged. Empty and one-element ranges perform no comparisons.

Sorting uses the existing unstable heapsort and takes O(n log n) comparisons.
Equal elements need not retain their original order. A comparator exception may
leave the selected range partially reordered, while every element stays owned.
Keep comparison ordering consistent and do not mutate storage from the comparator.
Reversal takes linear time in the selected range length.

Selective builds use `Collections/vector_ordering.gf` and
`Collections/span_algorithms.gf`. Default sorting also uses
`Collections/Comparer.gf`. Freestanding forms take a vector pointer first.
