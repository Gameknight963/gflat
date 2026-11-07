# Rotating vector elements

Load `Collections/vector_ordering.gf`, `Collections/span_algorithms.gf`, and
`Collections/Comparer.gf` for vector ordering helpers.

`Vector<T>.Rotate(middle)` moves the prefix before `middle` after the remaining
suffix, preserving order within both parts. `Rotate(start, count, middle)` applies
the same operation to a subrange, where `middle` is relative to that subrange.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    for (int i = 0; i < 5; i++) values.Add(i);
    values.Rotate(2);
    if (values[0] != 2 || values[1] != 3 || values[4] != 1) return 1;
    values.Rotate(1, 3, 1);
    if (values[0] != 2 || values[1] != 4 || values[2] != 0 || values[3] != 3) return 2;
    return 0;
}
```

A split at zero or at the selected range's end is a no-op. Invalid ranges or
splits throw before moving elements. The operation supports noncopyable owners,
allocates no storage, and retains count and capacity. Work is linear in the
selected range length. Element borrows still refer to storage positions whose
values may change.

Free-function equivalents accept a `Vector<T>*` under `Standard::Collections`.
