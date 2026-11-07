# Searching a span for a set of values

`IndexOfAny` finds the first source element equal to any candidate.
`IndexOfAnyExcept` finds the first source element unequal to every candidate.
The `LastIndexOfAny` and `LastIndexOfAnyExcept` forms search from the end.
A missing result is the source `Length`.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[5] values = [3, 7, 3, 9, 4];
    int[2] candidates = [3, 9];
    Span<immutable(int)> source = new(&values[0], 5);
    Span<immutable(int)> choices = new(&candidates[0], 2);
    if (IndexOfAny<int>(source, choices) != 0) return 1;
    if (LastIndexOfAny<int>(source, choices) != 3) return 2;
    if (IndexOfAnyExcept<int>(source, choices) != 1) return 3;
    if (!ContainsAny<int>(source, choices) || !ContainsAnyExcept<int>(source, choices)) return 4;
    return 0;
}
```

`ContainsAny` and `ContainsAnyExcept` return whether their corresponding index
search finds an element. An empty source matches neither. With no candidates,
ordinary searches find nothing, while `Except` searches match every source element.

Each method has a final equality-callback overload. Default equality requires
`IEquatable<T>`. Equality borrows the source element first and a candidate second.
Candidates are compared in index order until a match. Source elements are visited
in the selected search direction. Duplicate candidates are allowed.

The spans may overlap. No elements are copied and no storage is allocated.
Noncopyable values are supported. Equality exceptions propagate before a result
is assigned. Keep both spans alive and stable, and do not mutate them from equality.
Worst-case work is proportional to source length times candidate count.

Selective builds load `Collections/span_set_search.gf`. Default equality also
uses `Collections/default_equality.gf` and `Collections/EqualityComparer.gf`.
