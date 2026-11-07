# Insert an enumerable into a vector

`Vector<T>.InsertRange(index, source)` accepts an `Enumerable<T, S, E>`.
It inserts values in enumeration order before `index`, which may equal Count.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    values.Add(1);
    values.Add(2);
    values.Add(3);
    values.InsertRange(1, values.AsEnumerable());
    int[6] expected = [1, 1, 2, 3, 2, 3];
    for (nuint i = 0; i < 6; i++) if (values[i] != expected[i]) return 1;
    return 0;
}
```

The index is validated before acquiring the source iterator. Input is captured
before target growth or element relocation. Sources borrowing the vector are
supported, and fresh enumerable results can be move-only values. The iterator
is destroyed before changing the vector.

Input, copy, iterator, or allocation failure preserves the target's values,
count, capacity, and existing element addresses. Copies and source callbacks
must not structurally mutate the vector through other aliases. Values retain
their ordinary owning or borrowing semantics.

Capture needs temporary storage proportional to the input length. Growth uses
the vector's geometric capacity policy and allocates the target buffer at most
once. Insertion then rotates the appended batch into place without copying
elements or allocating. Empty input still acquires and advances its iterator
but changes no target storage. Successful nonempty insertion invalidates
borrows to elements at and after the insertion point, or all element borrows
if capacity grows.

Selective builds include `Collections/vector_insert_enumerable_range.gf`,
`Collections/vector_add_enumerable_range.gf`, and
`Collections/span_algorithms.gf`, with the usual vector and enumerable
dependencies. The freestanding form uses `InsertRange(ref target, index, source)`.
