# Linked lists

`std::LinkedList<T>` owns a doubly linked sequence of separately allocated nodes.
Use `Vector<T>` when contiguous storage and indexed access matter; a linked list
is useful when existing node addresses must remain stable during insertion and
you need constant-time insertion or removal at a known node.

```csharp
using std;

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

- `AddFirst(value)` and `AddLast(value)` insert and return a borrowed `Node*`.
- `InsertBefore(node, value)` and `InsertAfter(node, value)` do the same at a
  known position. A live node belonging to another list is rejected.
- `Remove(node)`, `RemoveFirst()`, and `RemoveLast()` transfer the removed value
  to the caller and free its node. Ignoring the returned value destroys it
  normally. Endpoint removal from an empty list throws.
- `First` and `Last` return nullable borrowed node pointers. A node's `Next` and
  `Previous` traverse the list; `Value` is a ref property borrowing its element.
- `Count` and `IsEmpty` inspect the sequence without copying elements.
- `Clear()` destroys all values and frees all nodes. Destruction does the same
  and releases the list's state block.

Inserting an owning value follows normal copying and movement rules: a named
copyable value is copied; `move value` transfers it. Noncopyable owning values
work without a special adapter. Pointer elements do not transfer ownership of
their pointees to the list.

The default constructor allocates nothing. The first insertion lazily allocates
a small state block, then a node; subsequent insertions allocate one node each.
`Clear` retains the empty state block for reuse. A failed first node allocation
may leave this state allocated, but the list remains empty and reusable.
Allocation failure preserves existing values and links and cleans the incoming
argument normally.

The list and its identity-bearing nodes cannot be copied as values. Moving the
list transfers its state and nodes, leaving the source uninitialized. Existing
node handles still belong to the destination; the source can be reinitialized
as an independent empty list.

## Borrowing and iteration

Node handles and element references remain valid across insertion and list
moves, provided the current owning list remains alive. Removing a node invalidates
its handle and references to its value. `Clear` and destruction invalidate all
node handles and element references. Other nodes retain their addresses after a
removal.

Nodes are owned by the list. Never `delete` a borrowed node, pass it to
`Memory::Free`, or manually move its value out of storage. Use `Remove` to
transfer an element. Foreign-node checks apply to **live** nodes only: a freed
or stale pointer cannot safely be inspected or validated.

`foreach` uses the nested borrowing `Enumerator` and visits values from first to
last. It creates no allocations and does not copy the list. Reading a value
follows normal value semantics; its `Current` property itself returns a ref.
Do not mutate the list during enumeration. Moving its owner alone preserves
the captured node addresses, so iteration can continue while the new owner
remains alive. `Current` throws before the first successful `MoveNext` and after
enumeration finishes.
