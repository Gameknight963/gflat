# Deque

`Standard::Collections::Deque<T>` is an owning double-ended queue backed by a growable ring
buffer. It supports noncopyable owning values without per-element allocations.

```csharp
using Standard::Text;
using Standard; using Standard::Collections;

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

`AddFirst(value)` and `AddLast(value)` insert at either end. Named copyable
arguments copy normally; `move value` transfers an existing owner. The deque
relocates elements during growth instead of invoking copy constructors.
`RemoveFirst()` and `RemoveLast()` transfer the removed value to the caller;
ignoring that return destroys it normally. Both throw on an empty deque.

`TryRemoveFirst(ref result)` and `TryRemoveLast(ref result)` instead return false
for an empty deque and leave the initialized result unchanged. On success they
transfer the selected endpoint into the result without copying and destroy its
previous owned value normally. They retain the deque's capacity and require no
new storage. Pointer-valued elements retain ordinary borrowed-pointer semantics.

The output must not refer to the deque's backing storage. For a nonempty deque,
both methods check its address against the whole allocation, including unused
ring slots, and throw `Exception("Deque output must not refer to its storage")`
before removing anything if it aliases that region. This rejects `ref deque.Front`,
`ref deque.Back`, indexed elements, and iterator borrows. Empty deques return
false without inspecting or changing output. Keep output storage separate from
the deque object's own fields as well.

Replacement follows ordinary owning assignment, as with `Queue.TryDequeue`
and `Stack.TryPop`: the endpoint is removed and ring metadata updated before
the previous output value is destroyed. Destructors follow the language's
termination-boundary rule if an exception escapes; the Try methods do not catch
such a failure or roll the deque back.

`Front`, `Back`, and `this[index]` are checked ref properties: they borrow the
stored element rather than copying it. Index zero is the front. Mutable receivers
allow updating elements; immutable receivers expose immutable references. `Count`,
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

## Indexed insertion and removal

`Deque<T>.Insert(index, value)` inserts before the existing logical index;
`index == Count` appends. Valid insertion indices are `0..Count` inclusive.
`RemoveAt(index)` accepts `0..Count-1`, removes that element, and returns its
owner. Ignoring the returned value destroys it normally. Invalid indices throw
before changing deque contents. Arguments follow ordinary by-value copy/move
rules; constructing an argument can throw before the method begins.

The endpoints delegate to `AddFirst`/`AddLast` and `RemoveFirst`/`RemoveLast`.
Middle insertion grows first when required, then moves the logical suffix one
slot toward the back; middle removal moves it toward the front. Ring wrap does
not change logical order. Relocation transfers owners without element copies
or element destructor calls. Middle operations take linear suffix-move time.

Failed growth preserves existing elements, their allocation and deque metadata.
An owning insertion argument is still consumed and destroyed normally on failure.
An ordinary copy of an existing deque element is made before insertion begins,
so that copy remains valid across wrapped growth and suffix relocation. If its
copy constructor throws, the deque remains unchanged. Structural mutation
invalidates existing borrowed elements and iterators, as with other deque methods.

```csharp
using Standard; using Standard::Collections;

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

`deque.AsEnumerable()` creates the ordinary query wrapper over a small captured
`Deque<T>::View`, without copying the owning deque or any elements. The view
holds the storage address, capacity, head, and count at the time of the call.
Each enumeration starts an independent existing `Deque<T>::Enumerator` at the
captured front. Selective source-library builds also include
`Queries/Enumerable.gf`; query operations live in the usual separate query
extension sources.

```csharp
bool IsEven(int value) => value % 2 == 0;

Deque<int> values = new();
values.AddLast(1);
values.AddLast(2);
values.AddFirst(0);
var evens = values.AsEnumerable().Where(&IsEven);
```

Replacing an existing element is visible to subsequent reads of that position;
the view is not a snapshot of element values. Keep the deque alive and at the
same address, and avoid structural mutations, moves, or destruction while using
a query, its source view, or its iterators. This applies even when a mutation
retains the same allocation. There is no mutation counter. Temporary owning
deques must not supply queries that outlive the temporary, just as with borrowed
vector spans.

Only mutable owners expose this adapter. It is not callable through readonly or
immutable deque receivers, since the existing iterator can return writable
element references. Capturing a view and operations such as `Count()` and
`Any()` do not copy elements, including noncopyable owners. Operations that
consume `Current` by value retain their usual element-copy requirements. Early
termination destroys iterator metadata without consuming or destroying elements.

Insertion argument-copy failure, capacity overflow, and allocation failure leave
existing contents unchanged. In particular, failed growth preserves the previous
buffer and borrowed element addresses. Moving the deque transfers its allocation;
the deque itself does not implicitly deep-copy. Pointer elements do not own their
pointees, while managed elements keep their normal collector behavior.

Both-end insertion is amortized O(1); removal and indexing are O(1). Growth and
`Clear` are O(n). Although its allocation is contiguous, the logical contents may
wrap around its end, so the deque does not expose a contiguous span.
