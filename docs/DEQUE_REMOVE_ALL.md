# Remove deque elements by predicate

`Deque<T>.RemoveAll(predicate)` removes matching elements and returns the number
removed as `nuint`. Predicates borrow elements in front-to-back order, and
survivors retain their order.

```csharp
using Standard::Collections;

bool Even(immutable(int)* value) => *value % 2 == 0;

int main()
{
    Deque<int> values = new();
    for (int i = 0; i < 6; i++) values.AddLast(i);
    if (values.RemoveAll(Even) != 3) return 1;
    if (values.Count != 3 || values[0] != 1 || values[1] != 3 || values[2] != 5) return 2;
    return 0;
}
```

Removal takes linear work, allocates no storage, and retains capacity. It works
with wrapped ring buffers and noncopyable owning elements. Empty input invokes
no predicate.

Every slot remains initialized while predicates run. Elements selected for
removal are destroyed after predicate traversal, rather than immediately.
Predicates may inspect the live owners, but their temporary order can change
as retained elements move forward. They must not structurally mutate the deque
or retain element addresses across later callbacks.

If a predicate throws, matching elements from earlier successful predicate
calls are removed. The throwing element and unvisited suffix remain, after
retained processed elements, in original order. The original exception
propagates. Capacity is preserved, but compaction can move elements and the
ring's starting position, invalidating prior element borrows.

The operation is part of `Collections/Deque.gf` and needs no copying helper.
