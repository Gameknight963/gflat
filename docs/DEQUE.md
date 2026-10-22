# Deque

`std::Deque<T>` is an owning double-ended queue backed by a growable ring
buffer. It supports noncopyable owning values without per-element allocations.

```csharp
using std;

int main()
{
    Deque<String> pending = new();
    pending.AddLast(s"second");
    pending.AddFirst(s"first");
    pending.AddLast(s"third");
    String next = pending.RemoveFirst();
    return (int)next.Length - 5;
}
```

`AddFirst(value)` and `AddLast(value)` insert at either end. Named copyable
arguments copy normally; `move value` transfers an existing owner. The deque
relocates elements during growth instead of invoking copy constructors.
`RemoveFirst()` and `RemoveLast()` transfer the removed value to the caller;
ignoring that return destroys it normally. Both throw on an empty deque.

`Front`, `Back`, and `this[index]` are checked ref properties: they borrow the
stored element rather than copying it. Index zero is the front. Mutable receivers
allow updating elements; readonly receivers expose readonly references. `Count`,
`Capacity`, and `IsEmpty` report metadata. `Reserve(n)` reserves space for at least
`n` entries. `Clear()` destroys every element but retains capacity.
`ShrinkToFit()` reduces capacity to `Count`, preserving front-to-back order even
when the old ring wraps. Empty storage is released, and an already tight deque
does nothing. Shrinking relocates owners without copying or destroying them;
allocation failure preserves the old buffer and all borrows. Successful
relocation invalidates borrowed elements and iterators just like growth.

The nested `Enumerator` visits elements from front to back and exposes ref
`Current`. Reading it before a successful `MoveNext` or after exhaustion throws.
Iteration borrows the deque. Structural changes invalidate borrowed element
references and iterators; do not move or destroy the owner while using them.
There is no runtime mutation counter or lifetime checking for these borrows.

Insertion argument-copy failure, capacity overflow, and allocation failure leave
existing contents unchanged. In particular, failed growth preserves the previous
buffer and borrowed element addresses. Moving the deque transfers its allocation;
the deque itself does not implicitly deep-copy. Pointer elements do not own their
pointees, while managed elements keep their normal collector behavior.

Both-end insertion is amortized O(1); removal and indexing are O(1). Growth and
`Clear` are O(n). Although its allocation is contiguous, the logical contents may
wrap around its end, so the deque does not expose a contiguous span.
