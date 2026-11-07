# Removing a deque range

`Deque<T>.RemoveRange(start, length)` destroys the selected elements and preserves
the order of survivors. Indices start at the front. An empty range at `Count` is
valid. An invalid range throws before destroying or moving any element.

```csharp
using Standard::Collections;

int main()
{
    Deque<int> values = new();
    for (int i = 0; i < 6; i++) values.AddLast(i);
    values.RemoveRange(1, 3);
    if (values.Count != 3 || values[0] != 0 || values[1] != 4 || values[2] != 5) return 1;
    values.RemoveRange(values.Count, 0);
    values.RemoveRange(0, values.Count);
    if (!values.IsEmpty) return 2;
    return 0;
}
```

The operation retains capacity and allocates no container storage. Surviving
owners move from the shorter side of the removed range, so removing a prefix or
suffix does not move survivors. Noncopyable elements are supported. Removed
elements are destroyed from the end of the range toward the start. Reacquire
element borrows and iterators after removal.
