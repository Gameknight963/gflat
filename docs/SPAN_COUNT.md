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
