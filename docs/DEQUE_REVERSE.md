# Reversing a deque

`Deque<T>.Reverse()` reverses the logical order in place.
`Reverse(start, length)` reverses just that range, using element indices from the
front of the deque. An empty range at `Count` is valid. Invalid ranges throw
before moving elements.

```csharp
using Standard::Collections;

int main()
{
    Deque<int> values = new();
    values.AddLast(10);
    values.AddLast(20);
    values.AddLast(30);
    values.Reverse();
    if (values.Front != 30 || values.Back != 10) return 1;
    values.Reverse(1, 2);
    if (values[1] != 10 || values[2] != 20) return 2;
    return 0;
}
```

Reversal exchanges owners without copying them, supports noncopyable elements,
and does not allocate a replacement buffer. Count and capacity stay the same.
Work is linear in the range length, including when storage wraps around the ring.
Existing element borrows refer to storage positions whose values may change.
