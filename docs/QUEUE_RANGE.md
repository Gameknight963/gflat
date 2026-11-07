# Enqueue a FIFO batch

`Queue<T>.EnqueueRange` appends a span or enumerable in input order. A span
copies its values. An enumerable can also produce fresh move-only values.

```csharp
using Standard::Collections;

int main()
{
    Queue<int> queue = new();
    queue.Enqueue(1);
    Vector<int> batch = new();
    batch.Add(2);
    batch.Add(3);
    queue.EnqueueRange(batch.AsImmutable());
    for (int expected = 1; expected <= 3; expected++)
        if (queue.Dequeue() != expected) return 1;
    return 0;
}
```

The operation captures the complete input before changing the queue. This permits
`queue.EnqueueRange(queue.AsEnumerable())`, including when the ring is wrapped
or needs to grow. Captured values need temporary storage proportional to the
batch length. The queue reserves space for the combined count once and retains
existing capacity when it already fits.

Input, element-copy, iterator, or allocation failure leaves the queue's contents,
capacity, and existing element addresses unchanged. The source iterator is
destroyed before the queue changes. Copies and source callbacks must not mutate
the queue through another alias. Values follow the element type's usual owning
or borrowing semantics.

Empty spans do nothing. Empty enumerables still acquire and advance their
iterator, but allocate no staging storage. Count overflow throws before reading
the element that would exceed the limit.

Selective builds include `Collections/Queue.gf` and
`Collections/queue_enqueue_range.gf`, with the relevant span or enumerable
dependencies. Including these files does not require copy support for a
move-only queue. The span overload requires copying when used.

The freestanding form takes a queue pointer first:
`Standard::Collections::EnqueueRange(&queue, batch.AsImmutable())`.
