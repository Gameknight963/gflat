# Append an enumerable to a vector

`Vector<T>.AddRange` accepts `Enumerable<T, S, E>` as well as the existing
span input. Values are appended in enumeration order.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    values.Add(1);
    values.Add(2);
    values.AddRange(values.AsEnumerable());
    if (values.Count != 4 || values[2] != 1 || values[3] != 2) return 1;
    return 0;
}
```

The enumerable overload captures the complete input into temporary storage
before changing the target. This supports input borrowing the same vector,
even when appending requires growth. The iterator is destroyed before target
growth starts. Fresh enumerable results can be move-only values.

Iterator, input, copy, or allocation failure preserves the target's elements,
count, capacity, and existing element addresses. Source callbacks and copies
must not structurally mutate the target through another alias. Values retain
their ordinary owning or borrowing semantics.

Capturing needs temporary storage proportional to the input length. The target
grows at most once for the combined count, reusing existing capacity when
sufficient and retaining the vector's geometric growth policy otherwise.
Empty input still acquires and advances its iterator, but allocates no staging
storage and does not change the target. Count overflow throws before reading
the element that would exceed the limit.

These failure semantics apply to the enumerable overload. The existing span
overload can retain a successfully appended prefix if a later element copy
throws.

Selective builds include `Collections/vector_add_enumerable_range.gf` with
the usual vector and enumerable dependencies. The freestanding form uses
`AddRange(ref target, source)`.
