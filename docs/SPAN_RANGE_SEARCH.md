# Searching spans by an inclusive range

`IndexOfAnyInRange` finds the first element between two inclusive bounds.
`LastIndexOfAnyInRange` finds the last one. The `IndexOfAnyExceptInRange` and
`LastIndexOfAnyExceptInRange` forms find elements outside those bounds.
A missing result is the source `Length`.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[5] values = [2, 5, 7, 9, 12];
    Span<immutable(int)> source = new(&values[0], 5);
    int lower = 5;
    int upper = 9;
    if (IndexOfAnyInRange<int>(source, &lower, &upper) != 1) return 1;
    if (LastIndexOfAnyInRange<int>(source, &lower, &upper) != 3) return 2;
    if (!ContainsAnyExceptInRange<int>(source, &lower, &upper)) return 3;
    return 0;
}
```

`ContainsAnyInRange` and `ContainsAnyExceptInRange` report whether the matching
index search succeeds. Empty sources match neither and invoke no comparisons.
With consistent total ordering, reversed bounds form an empty range, so every
element is outside it. Bounds are not reordered or validated.

Each method has an explicit comparator overload as its final argument. Default
ordering requires `IComparable<T>`. Comparisons borrow the source element first
and a bound second. The lower bound is checked first. The upper comparison is
skipped when the lower comparison decides the result for that element.

The source and bounds can alias. Elements and bounds are not copied, so
noncopyable values are supported. These linear searches allocate no storage.
Comparison exceptions propagate before assigning a result. Keep storage alive
and stable, and do not mutate values or bounds from the comparator.

Selective builds use `Collections/span_range_search.gf`. Default ordering also
uses `Collections/default_ordering.gf` and `Collections/Comparer.gf`.
