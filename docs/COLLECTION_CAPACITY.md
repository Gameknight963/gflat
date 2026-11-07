# Controlling collection capacity

`Vector<T>`, `Deque<T>`, `Queue<T>`, `Stack<T>`, `PriorityQueue<T>`,
`SortedMap<K, V>`, and `SortedSet<T>` expose
`EnsureCapacity(nuint requested)`. It reserves at least the requested number
of elements and returns the resulting capacity. A request below the current
capacity does not shrink storage.

```csharp
using Standard::Collections;

int main()
{
    Queue<int> pending = new();
    nuint capacity = pending.EnsureCapacity(100);
    if (capacity < 100) return 1;
    pending.Enqueue(42);
    pending.TrimExcess();
    if (pending.Capacity != 1 || pending.Dequeue() != 42) return 2;
    pending.ShrinkToFit();
    if (pending.Capacity != 0) return 3;
    return 0;
}
```

`TrimExcess()` reduces capacity to `Count` only when `Count` is below
`floor(Capacity * 9 / 10)`. This avoids relocation for a small saving.
The threshold uses integer arithmetic without multiplying the full capacity.
For capacities of zero or one the threshold is zero, so trimming does nothing.
Use `ShrinkToFit()` to unconditionally reduce capacity to `Count`, including
releasing an empty buffer. `Clear()` alone retains capacity.

These operations move elements without copying them and work with noncopyable
owners. Deque order, queue order, stack order, and heap ordering are preserved. A relocation
invalidates element pointers, spans, and iterators. If allocation fails, the
original capacity and elements remain intact. Heap and sorted collection capacity
changes do not call their comparators.
Sorted containers preserve their existing comparer order.

The existing `Reserve` API remains available when the resulting capacity is
not needed. These methods belong to the core collection types.
