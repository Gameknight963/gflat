# Copying vector elements into existing storage

`Vector<T>.CopyTo` assigns elements into an initialized destination span. It can
copy the whole vector, start at a destination index, or select a source range.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> source = new();
    source.Add(10);
    source.Add(20);
    source.Add(30);
    Vector<int> target = new();
    for (int i = 0; i < 5; i++) target.Add(0);
    source.CopyTo(target.AsSpan(), 1);
    source.CopyTo(1, target.AsSpan(), 0, 2);
    if (target[0] != 20 || target[1] != 30 || target[4] != 0) return 1;
    if (source.TryCopyTo(target.AsSpan().Slice(4))) return 2;
    return 0;
}
```

## Selecting a range

- `CopyTo(destination)` copies all elements to the start of the destination
- `CopyTo(destination, destinationIndex)` copies all elements starting at that index
- `CopyTo(sourceIndex, destination, destinationIndex, count)` copies the selected range
- `TryCopyTo(destination)` returns false without writing when the destination is too short

Invalid source ranges, destination indices, or insufficient space make `CopyTo`
throw before copying any elements. Indices may equal their respective lengths
for an empty copy. Destination elements outside the copied range remain unchanged.
Neither vector's `Count` or capacity changes. Reserved but uninitialized vector
capacity is not a valid destination.

## Ownership and failures

Copying supports overlapping storage, including slices of the source vector.
An exact self-copy does nothing. Other copies assign elements in a direction that
preserves unread source values. Copy constructors must not move, resize, or
destroy either backing container during the operation.

Elements must support copying. Owning values use their copy constructors, while
pointer elements copy pointers. A copy can allocate or throw according to its
element type. `TryCopyTo` propagates such failures. If a copy throws, completed
assignments remain and the current and unvisited destination elements retain
their previous values. For overlapping ranges, those completed assignments also
change the source storage. The operation itself allocates no temporary buffer.

The source may be immutable. Include `Collections/vector_copy.gf` and
`Memory/span_copy.gf` in explicit source lists. These optional files do not
require copying from vectors whose copy methods are never used. Freestanding
forms take an `immutable(Vector<T>)*` before the same method arguments.

Use [GetRange](VECTOR_GET_RANGE.md) to create a new owning vector instead.
