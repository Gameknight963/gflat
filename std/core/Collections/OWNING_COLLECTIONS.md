# Owning collections

Default `Vector<T>()` construction is `nothrow`. It initializes an empty header
with zero `Count` and `Capacity`, allocates no element storage, and constructs no
elements of `T`. Destroying that empty vector destroys no elements. This applies
to primitive, owning, and managed reference element types. The contract covers
empty header initialization; operations on elements and collector hooks retain
their own allocation and failure behavior.

`Vector<T>` owns contiguous elements. `Add(value)` appends, `Insert(index, value)`
inserts before an existing element (or appends at `Count`), and `RemoveAt(index)`
transfers the removed value to the caller while preserving the order of survivors.
Ignoring that return value destroys it normally. `Truncate(length)` destroys the
tail and keeps capacity; it rejects lengths greater than the current count.
`RemoveRange(start, length)` destroys a window in reverse order, shifts its
surviving suffix once, and keeps capacity. For example, a token buffer can discard
an already processed middle window without repeatedly shifting the same suffix
with `RemoveAt`. It neither allocates nor copies elements, including noncopyable
owners. The entire range is checked before destruction; an empty range is a
no-op even at `Count`, while a start beyond `Count` is invalid.
`ShrinkToFit()` explicitly releases unused peak storage, setting `Capacity` to
`Count`. An empty vector frees its buffer; an already tight vector does nothing.
Otherwise it allocates an exact-size buffer and relocates survivors without
copies or destruction. Failed allocation preserves contents, capacity, and
existing borrows. Successful shrinking invalidates borrowed pointers, spans,
and iterators just like capacity growth. Clearing or removing elements alone
continues to retain capacity.
`RemoveAll(predicate)` removes matching elements and returns their count, keeping
survivors in their original order. Its callback borrows the current element as
`immutable(T)*`; no element is copied and no container storage is allocated.
Each original element is tested once, in order, unless the callback throws.
Earlier matches are then removed, while the failing element and unvisited suffix
remain in order; the original exception propagates and the vector stays valid.
All slots remain initialized during callbacks, although their intermediate
ordering may change. Matched owners are destroyed after testing stops, in an
unspecified order. Callbacks must not structurally mutate or move the vector,
invoke `RemoveAll` recursively, or retain element borrows across callbacks. Element
destructors have the language's usual termination-boundary behavior. Capacity
is retained; any completed removal invalidates iterators and element borrows.
Middle insertion and removal relocate elements instead of invoking copy
constructors. The insertion argument follows ordinary value semantics and is
prepared before the vector changes, so inserting a copy of an existing element
works even when insertion grows the buffer. An explicit move source must not
refer to an element still owned by that vector.
`Pop()` transfers the last element, throwing if empty. `TryPop(ref result)`
returns false for an empty vector without touching the initialized output.
On success it moves the last owner into that output, updates `Count` before
destroying the output's previous value, and retains `Capacity`. No element copy
or container allocation is needed. Normal element cleanup effects still apply.
For nonempty vectors, output inside any part of the backing allocation, including
spare capacity, throws `Exception("Vector output must not refer to its storage")`
before removing or destroying anything. Output must be a separate complete
initialized value, not a reference into the vector's header or owned storage.
The empty check precedes the alias check, matching `Stack.TryPop`.

`SwapRemoveAt(index)` also returns the removed owner, but fills its slot with
the last element in constant time. It changes survivor order and is useful when
that order does not matter.

All middle mutations invalidate iterators and views of the vector. Allocation
failure, invalid indices, and a failing argument copy preserve existing contents.

With `using std;`, `vector.AddRange(immutableSpan)` copies a range into the tail.
A span borrowed from that same vector is supported, including across growth;
the implementation preserves its offset and rebinds it after reserving storage.
Empty ranges do nothing. Allocation failure before copying preserves contents.
Growth uses the vector's geometric capacity policy, so repeated small range
appends retain amortized growth rather than reallocating for each range.
If an element's copy constructor throws, the successfully appended prefix stays
in the vector and all earlier elements remain valid; capacity may have grown.
Copy constructors must not mutate the vector or source through other aliases.

`AddRange` is a ref-receiver extension because it requires copyable elements.
It is instantiated only when called; merely including it does not prevent using
`Vector<T>` with noncopyable owning elements. The vector's instance operations
continue to support those elements through explicit movement.

`vector.Resize(count, fill)` changes the number of initialized elements. Include
`Collections/vector_resize.gf`. It preserves the shared prefix, destroys the tail
when shrinking, and appends ordinary copies of `fill` when growing. Capacity is
retained on shrink; growth follows `Add`'s geometric capacity policy.

The by-value fill argument is prepared before the call, including for an equal
or smaller count. A fill copied from an existing vector element is therefore
captured before growth can relocate storage or shrink can destroy that element.
Do not explicitly move an element that is still owned by the vector.

A failing argument copy leaves the vector unchanged. If a later element copy or
allocation fails during growth, the successfully appended prefix remains valid;
capacity may have grown and existing borrows may have been invalidated. Copy
constructors must not structurally mutate the target through another alias.
`Resize` requires copying even when a particular call shrinks. Noncopyable vectors
can continue to use `Truncate`, `Add(move value)`, and their other instance methods.
No default construction of `T` is involved.
The fill argument participates in type inference: for `Vector<nuint>`, use a
typed `(nuint)0` fill or call `Resize<nuint>(count, 0)` explicitly.

```cpp
using std; using std::Collections;
int main()
{
    Vector<nuint> row = new();
    row.Resize(8, (nuint)0);
    row[0] = 7;
    row.Resize(12, (nuint)0);
    if (row.Count != 12 || row[0] != 7 || row[11] != 0) return 1;
    row.Resize(3, (nuint)0);
    if (row.Count != 3) return 2;
    return 0;
}
```

`vector.InsertRange(index, immutableSpan)` copies a range before `index`, which
may equal `Count`. Include `Collections/vector_insert_range.gf` together with
`Collections/vector_add_range.gf` and `Collections/span_algorithms.gf`. Like `AddRange`,
this optional extension requires copyable elements only when called. Self-borrowed
ranges work across growth, including insertion within the source range.

The index is checked even for an empty source. All copies finish before existing
elements are reordered; insertion then relocates elements without further copies.
If allocation or copying fails, original contents, order, and count are preserved
and successfully appended copies are destroyed. Capacity may have grown and
existing borrows may have been invalidated even on failure. Copy constructors and
cleanup destructors must not structurally mutate the source or target through
aliases; their external side effects are outside this guarantee. Destructors retain
the language's normal termination behavior. Successful insertion invalidates
element borrows and iterators. Work is linear in the source length plus the
target suffix length, with no auxiliary element buffer.

## Copying borrowed spans

With `using std;`, `source.AsImmutable().CopyTo(destination)` assigns copies into
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
`Memory/span_copy.gf`.

`source.TryCopyTo(destination)` has the same receiver and element-copy contract
as `CopyTo`, but returns false when the destination is too short. That length
check happens before accessing either data buffer or invoking any element copy;
neither span's elements are changed on that false result. Empty copies and exact
self-copies succeed. Successful copies leave any extra destination elements
unchanged and preserve the same overlap-safe assignment order as `CopyTo`.

This is a capacity check, not an exception boundary. A copy constructor or
allocation failure still propagates; assignments completed before that failure
remain committed under the existing `CopyTo` rules. The element type must support
the same ordinary copying even if a particular call would return false. Use
`view.AsImmutable().TryCopyTo(destination)` for a mutable source view; a readonly
or immutable destination cannot be written through this helper.

`span.Fill(value)` assigns a snapshot of `value` to every initialized element,
in increasing index order. For example, `work.AsSpan().Slice(2, 4).Fill(0)` resets
four existing work slots. The value is an ordinary by-value parameter: its copy
is prepared even for an empty span, and it may borrow an element of the target
because that snapshot precedes all assignments. Each element assignment uses
its normal copy constructor and destroys the replaced owner. If the parameter
copy fails, no elements change; if a later element copy fails, completed prefix
assignments remain and other elements stay valid. Copy constructors must not
mutate the destination through another alias.

`Fill` allocates no container storage, although value copies may allocate. It
requires mutable, copyable elements only when called, and lives separately in
`Memory/span_fill.gf`. It does not initialize raw storage.

## Stable sorting

`StableSort<T>(values, compare)` sorts a mutable `Span<T>` while retaining the
original order of elements whose comparison is zero. For example,
`StableSort<Record>(records.AsSpan(), CompareByTimestamp)` retains input order
among records with the same timestamp. Include `Collections/stable_sort.gf` and
its ordinary `Vector`/`Span` dependencies when selecting source files.

The comparator has type `int(immutable(T)*, immutable(T)*)*`: a negative result
orders the first value before the second, zero treats them as equivalent, and a
positive result orders the second first. It must define a consistent ordering.
It must not mutate elements, move or structurally mutate their owner, or sort
overlapping storage recursively through another alias. Borrowed element addresses
must not be used to track logical values across sorting: the storage stays in
place, but its elements change positions.

Sorting an empty or singleton span does not allocate or invoke the comparator.
Otherwise the operation allocates two index buffers, each holding `Length`
`nuint` values, with checked allocation sizes. It makes O(n log n) comparisons
before relocating any elements. Allocation or comparator failure preserves the
original element contents and order and releases all scratch storage; comparator
side effects outside the span are outside that guarantee. Once comparisons finish,
the resulting permutation is applied with typed moves, without element copies,
element destruction, additional allocation, or callbacks. Noncopyable owners are
supported and continue to be destroyed normally by their original owner. An
inconsistent comparator gives no ordering guarantee, but the operation still
preserves every original owner in the result permutation.

## Queue and stack

`Stack<T>` owns a last-in, first-out sequence backed by a `Vector<T>`.
`Queue<T>` owns a first-in, first-out sequence backed by a ring buffer.
Both start empty without allocating. `Count`, `Capacity`, and `IsEmpty`
describe their current state; `Reserve` grows capacity without changing contents.
`Queue<T>.ShrinkToFit()` reduces capacity to `Count` while preserving FIFO order,
including wrapped storage; an empty queue frees its buffer. A tight queue does
nothing. Successful relocation invalidates borrowed elements and iterators;
failed allocation preserves contents, capacity, and existing borrows. It moves
owners without invoking copy constructors or destroying elements.

`Stack<T>.ShrinkToFit()` also reduces capacity to `Count`, preserving stack
order without copying or destroying elements. Successful relocation invalidates
borrowed elements and views; allocation failure preserves them and the original
contents and capacity. A tight stack does nothing, and a drained stack frees its
retained buffer without allocating. Removal and `Clear` retain capacity until
shrinking is requested.

```csharp
using std; using std::Collections;

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
The output must not borrow the source collection's backing storage. For nonempty
stacks, queues, and priority queues, these methods check its address against the
entire allocation and throw before removing an element or invoking a comparator
when the output aliases that storage. This includes `ref stack.Top`,
`ref queue.Front`, and other elements reached through iterators. An empty
collection still returns false without touching or checking the output.

`Clear` destroys every remaining element but retains allocated capacity.
Destruction destroys remaining elements and releases storage. Pointer elements
follow ordinary pointer semantics: the collection does not delete pointees.
Collections themselves can be moved, but do not provide implicit deep copies.

Enumeration borrows storage and visits queue elements in FIFO order and stack
elements from top to bottom. Keep the collection alive and structurally unchanged while
using an iterator or borrowed element. Structural mutation invalidates these borrows;
there is no runtime mutation counter. The nested `Enumerator.Current` property
throws before the first successful `MoveNext` and after enumeration ends.

`AsEnumerable()` exposes the same order to lazy queries without copying the
owning queue/stack or its elements. It stores a nested borrowed `View`: ring
metadata for a queue, or a captured span for a stack. Fresh enumerations have
independent cursors, and ordinary element edits through valid aliases remain
visible. Captured views do not track a later count or extend the owner's lifetime.
Structural mutation, moving, or destroying the container invalidates them.
View construction and iteration allocate nothing; callbacks and value copies
retain their normal behavior. Include `Queries/Enumerable.gf` when selecting
these container sources explicitly, plus query operator sources actually used.

Noncopyable elements support view construction, direct borrowed iterator access,
`Count()`, and `Any()`. Operators whose `Current` returns an element by value can
require copying during generic specialization, even before a terminal reads it.
For example, a borrowed noncopyable sequence cannot currently use `Take` merely
to count a prefix. These adapters do not change that existing query limitation.

Growth checks capacity arithmetic and throws `BadAllocException` on allocation
failure. Failed allocation preserves existing elements. The insertion argument
still receives normal cleanup when insertion throws.

## Packed bits

`BitSet` owns a resizable sequence of packed bits. `Length` is the number of
addressable bits; `CountSetBits()` counts bits that are true. Indexing checks
bounds and produces a bool rather than a reference into packed storage.
`HasAnySet()` and `HasAllSet()` test occupancy without counting every set bit,
stopping when a word determines the answer. They work on immutable sets, allocate
nothing, and are `nothrow`. An empty set has no set bits and satisfies
`HasAllSet()` because it has no clear logical bits. Unused final-word padding
does not contribute to either answer.

```csharp
using std; using std::Collections;

int main()
{
    BitSet visited = new(100);
    visited[42] = true;
    visited.Flip(7);
    if (!visited.Overlaps(&visited)) return 1;
    nuint sum = 0;
    foreach (nuint index in visited) sum += index; // 7, then 42
    return (int)sum - 49;
}
```

`Resize` preserves the shared prefix, discards removed bits, and clears new bits.
`SetAll(bool)` fills the logical range; `Clear()` clears it without changing
length. Ordinary copying and `Clone()` make independent owned copies. Copying
allocates packed storage for nonempty sets; an empty copy requires no allocation.
Allocation failure leaves the source and an existing assignment target unchanged.
Moving still transfers ownership without copying. `UnionWith`,
`IntersectWith`, `ExceptWith`, and `SymmetricExceptWith` modify this set and
require matching logical lengths; their argument is a borrowed immutable pointer.

`BitSet` implements `IEquatable<BitSet>` and `IHashable`. Immutable `Equals(&other)`
compares the fixed universe as well as its set bits: equal values must have the
same `Length` and every logical bit equal. Different lengths return false instead
of throwing. `GetHashCode()` includes the length and packed words, so equal sets
have equal hashes. Both observers are `nothrow` and allocate nothing. Padding
remains canonical zero through all owning mutation operations. Hashes are not
persistent identifiers, and unequal values can have the same hash. These contracts
allow independently copied bitsets to be ordinary hash keys. As with other mutable
keys, do not change a stored key through an alias while a collection relies on it.

```csharp
using std; using std::Collections;
int main()
{
    BitSet flags = new(65);
    flags[64] = true;
    HashMap<BitSet, int> snapshots = new();
    snapshots.Add(flags, 7); // The stored key is an independent copy.
    flags.Clear();
    BitSet key = new(65);
    key[64] = true;
    if (!snapshots.ContainsKey(&key) || snapshots[&key] != 7) return 1;
    return 0;
}
```

`Overlaps(&other)` tests whether two equal-length bitsets share any set bit.
For matching lengths it copies no owner, allocates nothing, changes neither
input, and scans packed words only until an intersection is found. Empty sets
return false; self-comparison agrees with `HasAnySet()`. Like the set operations,
different `Length` values throw `Exception` before inspecting word storage.
It is an immutable observer and borrows its argument through an immutable pointer.

`IsSubsetOf(&other)` tests whether every set bit in this bitset is also set in
`other`; `IsSupersetOf(&other)` tests the converse. For example, required
capabilities can be checked against enabled capabilities without cloning or
building a temporary difference. Both methods borrow immutable bitsets, copy no
owner, allocate nothing on valid calls, and leave both inputs unchanged. They
scan packed words until inclusion fails. Self-comparisons and two empty bitsets
return true.

Like `Overlaps` and the mutating algebra, these methods require equal `Length`
values and throw `Exception("BitSet lengths differ")` otherwise. A length mismatch
is rejected before word storage is read. This is a fixed-universe contract;
it does not treat a shorter bitset as implicitly extended with clear bits.
`Equals` continues to return false, rather than throw, for different lengths.

```cpp
using std; using std::Collections;
int main()
{
    BitSet required = new(64);
    required[1] = true;
    required[7] = true;
    BitSet enabled = new(64);
    enabled[1] = true;
    enabled[7] = true;
    enabled[12] = true;
    immutable(BitSet)* requirements = &required;
    if (!requirements.IsSubsetOf(&enabled)) return 1;
    if (!enabled.IsSupersetOf(&required)) return 2;
    enabled[7] = false;
    if (required.IsSubsetOf(&enabled)) return 3;
    return 0;
}
```

`ShiftLeft(count)` moves each set bit toward a higher index; `ShiftRight(count)`
moves it toward a lower index. Both keep `Length`, discard bits shifted past an
end, and fill the vacated positions with zero. A zero shift changes nothing;
any count at least `Length` clears the set, including the largest `nuint` count.
They process words in place without allocating. Padding remains clear, so a
later `Resize` cannot bring discarded bits back.
`SetAll`, `Clear`, both shifts, and `CountSetBits` are `nothrow`; they neither
allocate nor invoke user callbacks.

`SetRange(start, count, value)` sets or clears a consecutive range;
`FlipRange(start, count)` toggles it. They process affected words without
allocating. Both validate the complete range before writing, so an invalid range
leaves all bits unchanged. A zero-length range is valid anywhere up to `Length`,
including on an empty set. Neither operation changes the logical length.

Enumeration yields **indices of set bits**, in ascending order, and borrows
storage. `AsEnumerable()` provides lazy queries over those indices through a
nested `BitSet::View`, capturing only the word pointer and bit length. It works
through mutable, shallow readonly, and immutable receivers; no owner is copied,
no allocation is made, and no pointer to the BitSet object is retained. For
example, `visited.AsEnumerable().Where(IsVisible).Take(10)` scans only far enough
to find ten matching set-bit indices.

Each enumeration starts again at zero using the captured length, so changes to
bit contents are visible on later enumerations. An active enumerator reloads
words as it advances: edits ahead of its position may change future results;
edits behind it do not repeat indices. Its current index stays cached until
`MoveNext`. Keep the original storage alive: resizing, moving, or destroying the
owning BitSet invalidates these borrows. Queries and views do not own or extend
the storage's lifetime. The public `View(storage, bitLength)` constructor also
accepts caller-owned words; null is valid only for zero length, otherwise provide
at least `ceil(bitLength / 32)` readable words for every later enumeration.

Explicit source selection now requires `Queries/Enumerable.gf` alongside
`Collections/BitSet.gf`, `Collections/IEnumerable.gf`, `IEquatable.gf`, `IHashable.gf`, and
`Hashing/primitive_hashing.gf`; query operators additionally use
their ordinary collection source files. Failed allocation during `Resize`
preserves both the original bits and length. Padding bits outside the logical
length are always kept clear.

### Finding a matching bit

`FindIndex(value, start = 0)` returns the first logical bit equal to `value` at or
after `start`, or `Length` if none matches. Start may equal `Length`, including on
an empty set; a larger start throws before reading bit storage. This is an
immutable observer, allocates nothing, and changes no bits. It skips whole words
with no matches and excludes both earlier bits and unused final-word padding.

```cpp
using std; using std::Collections;
int main()
{
    BitSet occupied = new(65);
    occupied.SetRange(0, 64, true);
    nuint freeSlot = occupied.FindIndex(false);
    if (freeSlot == occupied.Length) return 1;
    occupied[freeSlot] = true;
    if (freeSlot != 64 || occupied.FindIndex(false) != occupied.Length) return 2;
    return 0;
}
```

The existing enumerator already efficiently yields set-bit indices. Use it for
ordinary enumeration. `FindIndex(false, start)` instead locates a clear/free bit
without walking prior set-bit indices or allocating a complemented copy. A start
offset also supports a direct resumed search for either bit value.

## Priority queue

`PriorityQueue<T>` owns a binary min-heap. Its constructor accepts a comparison
function borrowing two elements through `immutable(T)*` pointers. Return a
negative result when the left element should be removed first, zero for equal
priority, and a positive result otherwise. Equal-priority removal order is not
stable. Comparator state and stored priorities must remain consistent while
elements are in the heap.
Types implementing `IComparable<T>` can use `Comparer<T>::Compare` directly,
such as `PriorityQueue<int> pending = new(Comparer<int>::Compare);`.

`ShrinkToFit()` reduces backing capacity to `Count`, preserving the existing
heap without calling the comparator, copying owners, or destroying elements.
Successful relocation invalidates borrowed elements; allocation failure preserves
contents, capacity, and existing borrows. A tight queue does nothing; a drained
queue frees retained storage without allocating. Removal and `Clear` retain
capacity until shrinking is requested.

```csharp
using std; using std::Collections;

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
as the FIFO queue. `Top` borrows the next element through `ref immutable(T)`.
`Count`, `Capacity`, `IsEmpty`, `Reserve`, and `Clear` have their usual collection
meanings. It does not expose an iterator that could be mistaken for sorted
enumeration; repeated removal produces priority order.

Insertion and removal find their relocation paths before moving any existing
elements. If the comparator throws, existing contents and heap order are
preserved. Allocation failure also preserves contents. Comparators must not
mutate the queue or its elements, including through separate aliases.
