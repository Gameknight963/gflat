# Linked lists

`Standard::Collections::LinkedList<T>` owns a doubly linked chain of allocated
nodes. Use it for stable node addresses and constant-time insertion/removal at
a known node.

```csharp
using Standard;
using Standard::Collections;

int main()
{
    LinkedList<int> values = new();
    LinkedList<int>::Node* second = values.AddLast(2);
    values.InsertBefore(second, 1);
    values.InsertAfter(second, 3);
    second.Value = 20;
    int removed = values.Remove(second); // 20; the node itself is freed.
    return removed - 20;
}
```

## Operations and ownership

| Operation | Result |
| --- | --- |
| `AddFirst`, `AddLast` | Inserts and returns a borrowed Node pointer |
| `InsertBefore`, `InsertAfter` | Inserts beside a live node in this list |
| `Remove(node)`, `RemoveFirst`, `RemoveLast` | Returns the removed element and frees its node |
| `First`, `Last` | Nullable node pointers |
| Node `Next`, `Previous`, ref `Value` | Traversal and element access |
| `Reverse()` | Reverses links while preserving node addresses |
| `RemoveAll(predicate)` | Removes matches and returns their count |
| `Clear()` | Destroys elements and frees nodes |

Use `move` to insert noncopyable owners. Discarding a returned owner destroys it.
Endpoint removal throws on an empty list. Inserting beside a live foreign node
throws. A stale node pointer must not be used.

Each insertion allocates a node. The first also creates list state, which Clear
retains for reuse. Allocation failure preserves existing values and links.
The list is move-only. Moving transfers nodes to the new owner and preserves
existing node handles.

RemoveAll visits first to last and unlinks each match before destroying it.
Later callbacks can observe earlier removals. A predicate exception preserves
the current node and unvisited suffix. Callbacks must leave structure unchanged
through other aliases.

## Borrowing and iteration

Node handles and element borrows survive insertion, reversal, and owner moves.
Removing a node ends its lifetime. Use Remove to take its element. Keep the
responsible owner alive.

Foreach and AsEnumerable visit first to last. Avoid structural edits during
iteration, and recreate cursors after Reverse to capture the new order. The
query Reverse operation instead buffers a reversed result without editing the list.

Source: `core/Collections/LinkedList.gf`
