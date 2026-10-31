# Deque

`Standard::Collections::Deque<T>` owns a double-ended sequence in a growable ring
buffer.

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

int main()
{
    Deque<String> pending = new();
    pending.AddLast(s"second");
    pending.AddFirst(s"first");
    pending.AddLast(s"third");
    String next = s"keep";
    if (!pending.TryRemoveFirst(ref next)) return 1;
    return (int)next.Length - 5;
}
```

| Operation | Effect |
| --- | --- |
| `AddFirst`, `AddLast` | Inserts a value at either end |
| `RemoveFirst`, `RemoveLast` | Returns the removed owner, throwing when empty |
| `TryRemoveFirst(ref result)`, `TryRemoveLast(ref result)` | Transfers into initialized output, or returns false when empty |
| `Front`, `Back`, ref indexer | Borrows the stored element |
| `Reserve(n)` | Reserves at least n entries |
| `ShrinkToFit()` | Reduces Capacity to Count |
| `Clear()` | Destroys elements and retains capacity |

Index zero is the front. Use `move` to insert noncopyable owners. Discarding a
returned owner destroys it. Try removal preserves output on an empty deque and
destroys its previous value after a successful transfer.

Removal output must be separate from the deque and its entire allocation,
including unused ring slots. Structural changes invalidate element borrows and
iterators. Allocation failure preserves the original buffer and contents.

End insertion is amortized O(1), while end removal and indexing are O(1).
Growth and Clear take O(n). Logical contents can wrap around the allocation.

## Indexed insertion and removal

`Insert(index, value)` accepts indices through Count, with Count meaning append.
`RemoveAt(index)` returns an existing indexed owner. Invalid indices throw before
changing contents. Middle operations move the suffix and take linear time.

```csharp
using Standard;
using Standard::Collections;

int main()
{
    Deque<int> values = new();
    values.AddLast(1);
    values.AddLast(3);
    values.Insert(1, 2);
    int removed = values.RemoveAt(0);
    if (removed != 1 || values.Count != 2 || values[0] != 2 || values[1] != 3) return 1;
    return 0;
}
```

## Borrowed queries

`AsEnumerable()` captures a borrowed view of current storage and logical range:

```csharp
bool IsEven(int value) => value % 2 == 0;

Deque<int> values = new();
values.AddLast(1);
values.AddLast(2);
values.AddFirst(0);
var evens = values.AsEnumerable().Where(&IsEven);
```

Keep the owner alive and avoid structural changes while using a query or iterator,
even when allocation stays the same. Element edits remain visible. The adapter
requires a mutable owner because its iterator returns writable references.

Source: `core/Collections/Deque.gf`
