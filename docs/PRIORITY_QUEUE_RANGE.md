# Enqueue a batch of priority values

`PriorityQueue<T>.EnqueueRange` accepts `Span<immutable(T)>` or an
`Enumerable<T, S, E>`. The span overload copies input values. An enumerable
can also produce fresh move-only values.

```csharp
using Standard::Collections;

int Compare(immutable(int)* first, immutable(int)* second) => *first - *second;

int main()
{
    PriorityQueue<int> queue = new(Compare);
    Vector<int> input = new();
    input.Add(4);
    input.Add(1);
    input.Add(3);
    queue.EnqueueRange(input.AsImmutable());
    if (queue.Dequeue() != 1 || queue.Dequeue() != 3 || queue.Dequeue() != 4) return 1;
    return 0;
}
```

Input is captured into an owning staging buffer before the queue changes.
Sources borrowing the queue's elements are therefore safe through later growth
and reordering. Iterator acquisition, input, copying, or capture-allocation
failure preserves the queue's contents and capacity. Source iterators release
their resources before heap insertion starts. Empty spans do nothing, and an
empty enumerable allocates no staging storage.

An empty queue builds its heap in linear comparison work. Comparison failure
in that case also preserves the queue. Existing reserved capacity is retained
on success, and an existing buffer is reused when it fits the batch. A nonempty
queue inserts captured values in input order using the
ordinary Enqueue operation. Comparison or capacity-growth failure leaves the
successfully inserted prefix in the queue and preserves its heap invariants.
Equal-priority order is unspecified.

Element copies and comparers can allocate or throw. They must not structurally
mutate the queue or source through other aliases. The batch needs temporary
storage proportional to its actual length. Published values follow ordinary
owning or borrowing semantics for T.

Selective builds include `Collections/priority_queue_enqueue_range.gf`,
`Collections/PriorityQueue.gf`, and the relevant span or enumerable dependencies.
Copying requirements are instantiated only when the copying span form is used.
The freestanding form takes a queue pointer first: `EnqueueRange(&queue, input)`.
