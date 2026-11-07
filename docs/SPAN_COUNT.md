# Counting a value in a span

`Standard::Collections::Count` counts occurrences of a borrowed key in a span.
Supply an equality callback, or use the default overload for `IEquatable<T>`.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[5] values = [3, 7, 3, 9, 3];
    Span<immutable(int)> view = new(&values[0], 5);
    int key = 3;
    if (Count<int>(view, &key) != 3) return 1;
    return 0;
}
```

The explicit form is `Count<T>(values, key, equal)`. Equality receives the source
element first and the key second, once per element in index order. An empty span
returns zero without calling equality. The key may refer to an element in the
span. Elements and keys are not copied, so noncopyable values are supported.

Counting does not allocate or change the span. Equality exceptions propagate
before a result is assigned. Keep the backing storage alive and stable, and do
not mutate it from equality. Runtime is linear in the span length.

Selective builds load `Collections/span_algorithms.gf` for callback counting.
The default overload also uses `Collections/default_equality.gf` and
`Collections/EqualityComparer.gf`. Use `CountIf` for predicate-based counting.

## Counting a pattern

Pass another span to count non-overlapping occurrences of a pattern:

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[5] values = [1, 1, 1, 1, 1];
    Span<immutable(int)> view = new(&values[0], 5);
    if (Count<int>(view, view.Slice(0, 2)) != 2) return 1;
    if (Count<int>(view, new Span<immutable(int)>()) != 0) return 2;
    return 0;
}
```

The explicit callback form is `Count<T>(values, pattern, equal)`. An empty
pattern or a pattern longer than the source returns zero without comparisons.
After a match, searching resumes immediately after the matched range. Candidate
positions and pattern offsets are checked from left to right, with the source
element passed to equality first. The pattern may borrow part of the source.

Pattern counting copies no elements and allocates no storage. The worst-case
comparison count is proportional to source length times pattern length. Load
`Collections/span_subsequence_search.gf` for the callback form. Default equality
also uses the same files as value counting above.
