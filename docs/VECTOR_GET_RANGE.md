# Copying a vector range

`Vector<T>.GetRange(start, count)` returns a new owning vector containing the
selected elements in their original order.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    values.Add(10);
    values.Add(20);
    values.Add(30);
    Vector<int> middle = values.GetRange(1, 2);
    if (middle.Count != 2 || middle[0] != 20 || middle[1] != 30) return 1;
    middle[0] = 99;
    if (values[1] != 20) return 2;
    values = values.GetRange(2, 1);
    if (values.Count != 1 || values[0] != 30) return 3;
    return 0;
}
```

Bounds are validated before allocating result storage or copying elements. `start` may equal
`Count` only for an empty range. Invalid ranges throw. The result has independent
storage, though copying pointer elements still copies pointers rather than their
pointees. The source may be immutable.

Each selected element is copied once. This operation requires copyable elements
and is kept in `Collections/vector_get_range.gf`, so vectors of noncopyable values
can still use their other operations. The freestanding form is
`GetRange<T>(sourcePointer, start, count)`.

An empty range returns an empty vector without allocating. Nonempty results
reserve storage before copying. If allocation or an element copy fails, the
partially built result is destroyed and an assignment destination stays unchanged.
The source is not modified. Side effects of element copy constructors remain
observable. Do not mutate or relocate the source from a copy constructor.

To borrow a range instead, slice `AsSpan`, `AsReadOnly`, or `AsImmutable` and
observe the lifetime of the source storage.
