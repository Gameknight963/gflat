# Copying queue, stack, and deque elements

`CopyTo(destination)` assigns copies into an initialized `Span<T>`. Queues
and deques copy front to back, and stacks copy top to bottom, matching
enumeration order.
`CopyTo(destination, destinationIndex)` starts at the specified destination index.

Load `Collections/queue_copy.gf`, `Collections/stack_copy.gf`, or
`Collections/deque_copy.gf` from Standard's
core source root. These extensions require copyable elements only when called.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    Queue<int> queue = new();
    queue.Enqueue(10);
    queue.Enqueue(20);
    int[4] buffer = [-1, -1, -1, -1];
    Span<int> output = new(&buffer[0], 4);
    queue.CopyTo(output, 1);
    if (buffer[0] != -1 || buffer[1] != 10 || buffer[2] != 20) return 1;

    Stack<int> stack = new();
    stack.Push(10);
    stack.Push(20);
    if (!stack.TryCopyTo(output)) return 2;
    if (buffer[0] != 20 || buffer[1] != 10 || queue.Count != 2) return 3;
    Deque<int> deque = new();
    deque.AddFirst(20);
    deque.AddFirst(10);
    deque.CopyTo(output);
    if (buffer[0] != 10 || buffer[1] != 20) return 4;
    return 0;
}
```

`TryCopyTo` returns false if the destination is too short and leaves it unchanged.
`CopyTo` throws in that case or for an invalid destination index. Neither method
allocates a container buffer, but element copy constructors can allocate or throw.
If a copy throws, the completed destination prefix remains replaced and the
failing slot retains its old value. Source elements stay owned by the collection.

The destination prefix being written must not overlap the collection's backing
allocation, including spare capacity. Overlap throws before copying. Keep the
collection alive and structurally unchanged during element copies.
