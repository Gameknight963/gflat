# Copy set elements into an existing buffer

`HashSet<T>` provides optional `CopyTo(Span<T>)`,
`CopyTo(Span<T>, nuint destinationIndex)`, and `TryCopyTo(Span<T>)` extensions.
They copy stored elements in the set's current enumeration order. This order
is not sorted and can change after mutation or rehashing.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    HashSet<int> values = new();
    values.Add(4);
    values.Add(9);
    int[3] output = [77, 77, 77];
    Span<int> destination = new(&output[0], 3);
    values.CopyTo(destination, 1);
    if (output[0] != 77) return 1;
    if (output[1] + output[2] != 13 || output[1] == output[2]) return 2;
    if (values.TryCopyTo(destination.Slice(0, 1))) return 3;
    return 0;
}
```

Destination elements must already be initialized. `T` must be copyable.
Owning values are copied according to their normal copy semantics. Use
borrowed enumeration or `Find` for noncopyable elements.

`TryCopyTo` returns false before copying when the destination is too short.
`CopyTo` throws instead. Destination indices are validated even for empty sets.
An empty set otherwise succeeds without accessing destination storage.

The written destination range must not overlap any part of the set's backing
allocation, including unused buckets and metadata. An overlap throws before
copying. Copying does not invoke hashing or equality callbacks and allocates
no internal storage. Element copies can allocate or throw. A failed element
copy leaves earlier destination replacements completed, preserves the failing
slot and later slots, and leaves the set unchanged. Copy operations must not
structurally mutate the source set.

Selective builds include `Collections/HashSet.gf`, `Collections/HashMap.gf`,
and `Collections/hash_set_copy.gf` with their normal value and span dependencies.
The optional helpers do not add a copy requirement to borrowed set operations.
Freestanding forms take an immutable set pointer first, for example
`CopyTo(&values, destination)` or `TryCopyTo(&values, destination)`.
