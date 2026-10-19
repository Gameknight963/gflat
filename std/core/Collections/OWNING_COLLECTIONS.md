# Owning collections

`Vector<T>` owns contiguous elements. `Add(value)` appends, `Insert(index, value)`
inserts before an existing element (or appends at `Count`), and `RemoveAt(index)`
transfers the removed value to the caller while preserving the order of survivors.
Ignoring that return value destroys it normally. `Truncate(length)` destroys the
tail and keeps capacity; it rejects lengths greater than the current count.
Middle insertion and removal relocate elements instead of invoking copy
constructors. The insertion argument follows ordinary value semantics and is
prepared before the vector changes, so inserting a copy of an existing element
works even when insertion grows the buffer. An explicit move source must not
refer to an element still owned by that vector.
`SwapRemoveAt(index)` also returns the removed owner, but fills its slot with
the last element in constant time. It changes survivor order and is useful when
that order does not matter.

All middle mutations invalidate iterators and views of the vector. Allocation
failure, invalid indices, and a failing argument copy preserve existing contents.

With `using std;`, `vector.AddRange(readonlySpan)` copies a range into the tail.
A span borrowed from that same vector is supported, including across growth;
the implementation preserves its offset and rebinds it after reserving storage.
Empty ranges do nothing. Allocation failure before copying preserves contents.
If an element's copy constructor throws, the successfully appended prefix stays
in the vector and all earlier elements remain valid; capacity may have grown.
Copy constructors must not mutate the vector or source through other aliases.

`AddRange` is a ref-receiver extension because it requires copyable elements.
It is instantiated only when called; merely including it does not prevent using
`Vector<T>` with noncopyable owning elements. The vector's instance operations
continue to support those elements through explicit movement.

## Copying borrowed spans

With `using std;`, `source.AsReadOnly().CopyTo(destination)` assigns copies into
an existing `Span<T>`. The destination must contain initialized elements and
have room for every source element. Its unused tail stays unchanged. This is
typed copying: normal copy constructors run and replaced destination values are
destroyed. It is not construction into raw storage or byte copying.

The implementation handles overlapping spans in either direction. Exact self-copy
does nothing. Capacity is checked before invoking any copy constructor; a short
destination is unchanged. If a copy throws, earlier completed assignments remain
and other elements stay valid. Copying proceeds backward when the destination
starts at a higher address, so partial progress may be a suffix rather than a
prefix. Copy constructors must not mutate either collection through another alias.

`CopyTo` allocates no container storage, although individual copy constructors may
allocate. The views keep borrowing the same storage. Like `AddRange`, this copying
extension is checked only when called; spans of noncopyable values remain usable
without calling it. For source-library selection its file is
`Collections/SpanCopy.gf`.

## Queue and stack

`Stack<T>` owns a last-in, first-out sequence backed by a `Vector<T>`.
`Queue<T>` owns a first-in, first-out sequence backed by a ring buffer.
Both start empty without allocating. `Count`, `Capacity`, and `IsEmpty`
describe their current state; `Reserve` grows capacity without changing contents.

```csharp
using std;

int main()
{
    Queue<int> work = new();
    work.Enqueue(10);
    work.Enqueue(20);
    int first = work.Dequeue(); // 10

    Stack<int> history = new();
    history.Push(first);
    history.Push(20);
    int last = history.Pop(); // 20
    return first + last - 30;
}
```

`Enqueue` and `Push` follow ordinary value semantics: passing a named owning
value copies it when its type supports copying; passing `move value` transfers
it. `Dequeue` and `Pop` transfer the removed element to the caller. `Front`
and `Top` borrow an element through a ref property, so inspecting or modifying
it does not copy its resources. Empty removal or peeking throws `Exception`.
`TryDequeue(ref result)` and `TryPop(ref result)` return false for an empty
collection and leave the initialized result unchanged. On success they replace
it with the removed element, cleaning up its previous value normally.
The output must not refer to an element inside the collection being modified.

`Clear` destroys every remaining element but retains allocated capacity.
Destruction destroys remaining elements and releases storage. Pointer elements
follow ordinary pointer semantics: the collection does not delete pointees.
Collections themselves can be moved, but do not provide implicit deep copies.

Enumeration borrows storage and visits queue elements in FIFO order and stack
elements from top to bottom. Keep the collection alive and unchanged while
using an iterator or borrowed element. Mutation invalidates these borrows;
there is no runtime mutation counter. The nested `Enumerator.Current` property
throws before the first successful `MoveNext` and after enumeration ends.

Growth checks capacity arithmetic and throws `BadAllocException` on allocation
failure. Failed allocation preserves existing elements. The insertion argument
still receives normal cleanup when insertion throws.

## Packed bits

`BitSet` owns a resizable sequence of packed bits. `Length` is the number of
addressable bits; `CountSetBits()` counts bits that are true. Indexing checks
bounds and produces a bool rather than a reference into packed storage.

```csharp
using std;

int main()
{
    BitSet visited = new(100);
    visited[42] = true;
    visited.Flip(7);
    nuint sum = 0;
    foreach (nuint index in visited) sum += index; // 7, then 42
    return (int)sum - 49;
}
```

`Resize` preserves the shared prefix, discards removed bits, and clears new bits.
`SetAll(bool)` fills the logical range; `Clear()` clears it without changing
length. `Clone()` explicitly makes an independent owned copy. `UnionWith`,
`IntersectWith`, `ExceptWith`, and `SymmetricExceptWith` modify this set and
require matching logical lengths; their argument is a borrowed readonly pointer.

`SetRange(start, count, value)` sets or clears a consecutive range;
`FlipRange(start, count)` toggles it. They process affected words without
allocating. Both validate the complete range before writing, so an invalid range
leaves all bits unchanged. A zero-length range is valid anywhere up to `Length`,
including on an empty set. Neither operation changes the logical length.

Enumeration yields **indices of set bits**, in ascending order, and borrows
storage. Mutation invalidates the iterator. Failed allocation during `Resize`
preserves both the original bits and length. Padding bits outside the logical
length are always kept clear.

## Priority queue

`PriorityQueue<T>` owns a binary min-heap. Its constructor accepts a comparison
function borrowing two elements through `readonly(T)*` pointers. Return a
negative result when the left element should be removed first, zero for equal
priority, and a positive result otherwise. Equal-priority removal order is not
stable. Comparator state and stored priorities must remain consistent while
elements are in the heap.
Types implementing `IComparable<T>` can use `Comparer<T>::Compare` directly,
such as `PriorityQueue<int> pending = new(Comparer<int>::Compare);`.

```csharp
using std;

int Compare(readonly(int)* left, readonly(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    PriorityQueue<int> pending = new(Compare);
    pending.Enqueue(20);
    pending.Enqueue(5);
    return pending.Dequeue() - 5;
}
```

`Enqueue`, `Dequeue`, and `TryDequeue(ref result)` use the same ownership rules
as the FIFO queue. `Top` borrows the next element through a readonly ref property.
`Count`, `Capacity`, `IsEmpty`, `Reserve`, and `Clear` have their usual collection
meanings. It does not expose an iterator that could be mistaken for sorted
enumeration; repeated removal produces priority order.

Insertion and removal find their relocation paths before moving any existing
elements. If the comparator throws, existing contents and heap order are
preserved. Allocation failure also preserves contents. Comparators must not
mutate the queue or its elements, including through separate aliases.
