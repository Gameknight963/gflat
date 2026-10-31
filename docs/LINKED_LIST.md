# Linked lists

`Standard::Collections::LinkedList<T>` owns a doubly linked sequence of separately allocated nodes.
Use `Vector<T>` when contiguous storage and indexed access matter. A linked list
is useful when existing node addresses must remain stable during insertion and
you need constant-time insertion or removal at a known node.

```csharp
using Standard; using Standard::Collections;

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

- `AddFirst(value)` and `AddLast(value)` insert and return a borrowed `Node*`
- `InsertBefore(node, value)` and `InsertAfter(node, value)` do the same at a
  known position. A live node belonging to another list is rejected.
- `Remove(node)`, `RemoveFirst()`, and `RemoveLast()` transfer the removed value
  to the caller and free its node. Ignoring the returned value destroys it
  normally. Endpoint removal from an empty list throws.
- `First` and `Last` return nullable borrowed node pointers. A node's `Next` and
  `Previous` traverse the list. `Value` is a ref property borrowing its element.
- `Count` and `IsEmpty` inspect the sequence without copying elements
- `Reverse()` reverses the stored node order without allocating or moving values
- `RemoveAll(predicate)` removes matching nodes in first-to-last order and
  returns their count, borrowing each value through `immutable(T)*`
- `Clear()` destroys all values and frees all nodes. Destruction does the same
  and releases the list's state block.

Inserting an owning value follows normal copying and movement rules: a named
copyable value is copied. `move value` transfers it. Noncopyable owning values
work without a special adapter. Pointer elements do not transfer ownership of
their pointees to the list.

The default constructor allocates nothing. The first insertion lazily allocates
a small state block, then a node. Subsequent insertions allocate one node each.
`Clear` retains the empty state block for reuse. A failed first node allocation
may leave this state allocated, but the list remains empty and reusable.
Allocation failure preserves existing values and links and cleans the incoming
argument normally.

`RemoveAll(bool(immutable(T)*)* predicate)` tests each original node once, from
first to last. It copies no elements and allocates no list storage. A matching
node is unlinked and `Count` updated before its value is destroyed and its node
freed. Cleanup is immediate for each match, so later predicate calls can observe
completed earlier removals. This differs from Vector's deferred matched-owner
cleanup. Survivors keep their order and addresses. Existing handles to removed
nodes become invalid. An empty result retains the state block for reuse, and a
never-populated list remains allocation-free.

If the predicate throws, its original exception propagates. Earlier removals
remain committed. The current node and unvisited suffix stay in the list with
valid links and values. Predicates must not structurally mutate, move or destroy
the list, invoke `RemoveAll` recursively, or retain the current value borrow.
Element cleanup follows its normal destructor contract and must not invalidate
the remaining chain through another alias. The method is not `nothrow`: user
predicates can allocate or throw, and destructor termination boundaries remain
unchanged. These callback limits do not prevent inspecting the list's current
count, endpoints or valid surviving nodes.

The list and its identity-bearing nodes cannot be copied as values. Moving the
list transfers its state and nodes, leaving the source uninitialized. Existing
node handles still belong to the destination. The source can be reinitialized
as an independent empty list.

## Borrowing and iteration

Node handles and element references remain valid across insertion, reversal and
list moves, provided the current owning list remains alive. Removing a node invalidates
its handle and references to its value. `Clear` and destruction invalidate all
node handles and element references. Other nodes retain their addresses after a
removal.

Nodes are owned by the list. Never `delete` a borrowed node, pass it to
`Memory::Free`, or manually move its value out of storage. Use `Remove` to
transfer an element. Foreign-node checks apply to **live** nodes only: a freed
or stale pointer cannot safely be inspected or validated.

`foreach` uses the nested borrowing `Enumerator` and visits values from first to
last. It creates no allocations and does not copy the list. Reading a value
follows normal value semantics. Its `Current` property itself returns a ref.
Do not mutate the list during enumeration. Recreate cursors after `Reverse()`.
Moving its owner alone preserves
the captured node addresses, so iteration can continue while the new owner
remains alive. `Current` throws before the first successful `MoveNext` and after
enumeration finishes.

`AsEnumerable()` exposes the same first-to-last order to lazy queries. Its nested
`View` captures only the first node pointer, without copying the list or its
values. Each enumeration creates a fresh cursor. Edits through valid element
references remain visible. Do not insert, remove or reverse nodes while using the view.
Moving the owner preserves the node chain, so existing queries remain usable
while the destination owner lives. Reinitializing the moved-from variable creates
an independent list and does not retarget existing views.

View creation and iteration allocate nothing. Noncopyable elements support direct
borrowed iterator access, `Count()`, and `Any()`. Query operators that return
elements by value can require copying during generic specialization. That also
applies when the final consumer only counts their output. Callbacks and copying
terminals retain their ordinary allocation and exception behavior.

For explicit source selection, include `Queries/Enumerable.gf` alongside
`Collections/IEnumerable.gf` and `Collections/LinkedList.gf`, plus the operator sources used.

`Reverse()` takes O(n) time and O(1) extra storage and is `nothrow`. Each node
keeps its original value and address. `Count` stays unchanged. Empty and singleton
lists are no-ops. Recreate views after reversal to capture the new first node.
This changes the list itself. `list.AsEnumerable().Reverse()` instead creates a
buffered reversed query and leaves the list unchanged.
