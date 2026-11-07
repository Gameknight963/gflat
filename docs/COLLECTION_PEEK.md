# Copy the next collection value

`Queue<T>`, `Stack<T>`, and `PriorityQueue<T>` provide optional copying
`Peek()` and `TryPeek(ref output)` extensions in `Standard::Collections`.

| Container | Copied value |
| --- | --- |
| `Queue<T>` | Front of the FIFO queue |
| `Stack<T>` | Top of the stack |
| `PriorityQueue<T>` | Current minimum under its comparer |

`Peek()` returns a copy of the next element and throws if the container is empty.
`TryPeek(ref output)` returns true after copying into initialized output. An empty
container returns false and leaves output unchanged. No element is removed.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    Queue<String> pending = new();
    String next = s"previous";
    if (pending.TryPeek(ref next) || !next.Equals("previous")) return 1;
    pending.Enqueue(s"first");
    pending.Enqueue(s"second");
    String first = pending.Peek();
    if (!first.Equals("first")) return 4;
    if (!pending.TryPeek(ref next) || !next.Equals("first") || pending.Count != 2) return 2;
    pending.Clear();
    if (!next.Equals("first") || !first.Equals("first")) return 3;
    return 0;
}
```

## Ownership and errors

`T` must be copyable. The copy follows its ordinary value semantics. An owning
`String` result has independent storage, while a pointer value remains a
pointer. Use the borrowed `Front` or `Top` property when copying is unnecessary
or the element is noncopyable.

The copy is constructed before replacing output. A copying or allocation
failure preserves the existing output and container. Empty peeks perform no
copying or allocation. Nonempty peeks do not allocate internal container
storage, grow capacity, or invoke the priority comparer. Element copying can
still allocate or throw.

Queue and stack outputs can refer to stored elements, including the source
element itself. Replacing such an output updates that slot without changing
the container's count or order. Priority-queue outputs must be separate from
its entire storage allocation, including spare capacity. Violations throw
before copying because replacing a heap entry could invalidate its ordering.

## Source selection

The freestanding form takes an immutable container pointer first:
`TryPeek(&pending, ref output)` or `Peek(&pending)`.

Selective builds include the container and its corresponding extension:

| Container | Extension source |
| --- | --- |
| `Queue<T>` | `Collections/queue_peek.gf` |
| `Stack<T>` | `Collections/stack_peek.gf` |
| `PriorityQueue<T>` | `Collections/priority_queue_peek.gf` |

The copying helpers are separate from the base container implementations.
Borrowing and moving noncopyable elements remain available when these files
are included.
