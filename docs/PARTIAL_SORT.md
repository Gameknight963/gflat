# Sort a prefix by rank

`PartialSort<T>(values, count)` places the smallest `count` elements in sorted
order at the start of a mutable span. The remaining elements keep an unspecified
order. Equal elements need not retain their relative order.

The two-argument overload uses `IComparable<T>`. Pass a third argument of type
`int(immutable(T)*, immutable(T)*)*` to choose another ordering, such as descending.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[5] data = [8, 3, 1, 9, 2];
    Span<int> values = new(data, 5);
    PartialSort<int>(values, 3);
    if (data[0] != 1 || data[1] != 2 || data[2] != 3) return 1;
    return 0;
}
```

`count` must not exceed `Length`. An invalid count throws before comparisons or
relocation. Zero count leaves the span unchanged without calling the comparator.

The algorithm uses a heap within the selected prefix and allocates no temporary
buffer. Elements are relocated rather than copied, so noncopyable owners are
supported. If the comparator throws, the ordering may be partial, but the elements
remain initialized and owned by the caller. Use `Slice` to limit the range being
modified.
