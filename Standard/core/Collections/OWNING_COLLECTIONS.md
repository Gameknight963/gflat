# Owning collections

These containers own their stored values and release remaining elements at scope
exit. Empty constructors are `nothrow` and initialize an inline header. Ordered
containers also retain their comparison function. Storage and element
construction begin when a size is requested or an insertion grows the container.

## Vector mutations

| Operation | Effect |
| --- | --- |
| `Add(value)` | Appends a value |
| `Insert(index, value)` | Inserts before index, including Count for append |
| `RemoveAt(index)` | Returns the removed value and preserves survivor order |
| `SwapRemoveAt(index)` | Returns the removed value and fills its slot with the last element |
| `Truncate(length)` | Destroys the tail and retains capacity |
| `RemoveRange(start, length)` | Destroys a range in reverse order and shifts its suffix once |

A removed owning result transfers to the caller. Ignoring it runs normal cleanup.
Middle mutations relocate elements and invalidate views and iterators. The
insertion argument is prepared before mutation, so copying an existing element
into an insertion is supported. An explicit move must leave ownership coherent:
avoid moving an element that the same vector still owns.

Invalid indices and failed argument copies preserve contents. RemoveRange checks
the complete range before destruction. A zero-length range is valid through Count.
Truncate rejects lengths greater than Count.

### Capacity

Reserve grows capacity. Clear and removal retain it. ShrinkToFit reduces capacity
to Count and releases the buffer when empty. Growth and shrinking relocate
survivors, invalidating views on success. Allocation failure preserves contents,
capacity, and existing views.

### Filtering elements

`RemoveAll(predicate)` removes matches, preserves survivor order, and returns the
removed count. The predicate borrows `immutable(T)*` and tests original elements
once in order.

If it throws, earlier matches are removed while the failing element and unvisited
suffix remain. The vector stays valid and the original exception propagates.
Every slot remains initialized during callbacks, although intermediate order can
change. Removed owners are destroyed after testing stops, in an unspecified order.

Keep the vector structurally unchanged during callbacks. Release element borrows
before returning, and avoid recursive RemoveAll calls. Any completed removal
invalidates iterators and element views.

### Removing the last element

`Pop()` transfers the final value or throws when empty. `TryPop(ref result)` returns
false for an empty vector and leaves output unchanged. On success it removes the
last value and replaces the initialized output, retaining capacity. Count is
updated before the output's old value is destroyed.

Output must be separate from the vector header and backing allocation, including
spare capacity. A nonempty vector checks backing-storage aliases before removal.
An empty vector returns false before that check.

### Adding ranges

`AddRange(immutableSpan)` copies elements into the tail. A span from the same
vector is supported across growth. Empty input is a no-op.

Allocation failure before copying preserves contents. A later copy failure leaves
the successfully appended prefix and all original elements valid. Capacity may
have grown, invalidating old views. Copy constructors must leave source and
target structurally unchanged through other aliases.

The extension requires copyable elements when called. Other vector operations
accept noncopyable owners through movement.

### Resizing

`Resize(count, fill)` preserves the shared prefix, destroys a truncated tail, or
appends copies of fill. Capacity is retained on shrink. Fill is captured by value
before mutation, including when it comes from an existing element.

```cpp
using Standard;
using Standard::Collections;

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

A failed fill-argument copy preserves the vector. A later copy or allocation
failure leaves successfully appended elements in place, with possible capacity
growth. The fill overload requires a copyable type even when the call shrinks.

`Resize(count)` default-constructs new elements. It requires an accessible
zero-argument construction of T. Scalars are zero-initialized:

```cpp
using Standard;
using Standard::Collections;
using Standard::IO;

int main()
{
    Vector<char> buffer = new();
    buffer.Resize(4096);
    FileStream input = File::OpenTemporary();
    nuint read = input.Read(buffer.AsSpan().Data!, buffer.Count);
    buffer.Truncate(read);
    if (!buffer.IsEmpty || buffer.Capacity != 4096) return 1;
    return 0;
}
```

Growth reserves once, then constructs in ascending order. A constructor failure
destroys the newly appended prefix and restores the original count and contents.
Capacity may have grown, so reacquire views after failed growth as well.
External constructor effects remain visible. Use Truncate to shrink a type
without default construction.

### Inserting ranges

`InsertRange(index, immutableSpan)` copies a range before index, which may equal
Count. Self-borrowed ranges work across growth, including insertion inside the
source range.

Copies finish before original elements are reordered. A copy or allocation
failure preserves original contents, order, and count, while discarding completed
new copies. Capacity may have grown and invalidated views. Successful insertion
invalidates element views and iterators. Work is linear in source length plus
the target suffix length.

## Copying borrowed spans

`source.AsImmutable().CopyTo(destination)` assigns ordinary copies into initialized
destination elements. Replaced owners are destroyed. The destination must have
room for the complete source, and its extra tail stays unchanged.

Overlap is supported in either direction. Exact self-copy is a no-op. A short
destination fails before assignment. Copying runs backward when the destination
starts higher in memory, so a failing copy can leave a completed suffix. Other
slots stay valid.

`TryCopyTo` returns false for insufficient capacity and otherwise shares the same
copy behavior. Element-copy exceptions still propagate after any completed
assignments. Both operations require copyable elements.

`Fill(value)` assigns a captured copy into each element in ascending order.
Its by-value argument is prepared before mutation, including for an empty span.
It can therefore come from the destination itself. A later copy failure leaves
completed prefix assignments in place.

## Stable sorting

`StableSort(values, compare)` retains encounter order among comparator-equivalent
values. The comparator borrows two `immutable(T)*` arguments and defines a
consistent negative/zero/positive ordering.

For at least two elements, sorting allocates two Length-sized nuint index buffers
and finishes O(n log n) comparisons before relocating values. Allocation or
comparison failure preserves original contents and order. Empty and singleton
input needs no comparison or scratch allocation.

Callbacks must leave elements and their owner structurally unchanged. A borrowed
slot identifies storage, so a value can move away from it during sorting.
Noncopyable owners are supported. An inconsistent comparator loses the ordering
guarantee while retaining the original owners in the permutation.

## Queue and stack

`Queue<T>` owns a FIFO ring buffer. `Stack<T>` owns a LIFO sequence backed by a
vector:

```csharp
using Standard;
using Standard::Collections;

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

| Method | Ownership |
| --- | --- |
| `Enqueue` / `Push` | Takes a fresh value, copies a copyable local, or consumes an explicit move |
| `Dequeue` / `Pop` | Transfers the removed value to the caller |
| `Front` / `Top` | Borrows an element through a ref property |
| `TryDequeue(ref result)` / `TryPop(ref result)` | Replaces initialized output on success, leaves it unchanged when empty |
| `Clear` | Destroys elements and retains capacity |
| `ShrinkToFit` | Reduces capacity to Count and releases empty storage |

Empty removal or peeking throws Exception. Nonempty ref-output operations reject
an output address anywhere inside the backing allocation before removal. This
includes Front, Top, and iterator references. Empty Try calls return false before
the alias check.

Growth and shrinking preserve owners by relocation. Allocation failure preserves
contents and existing views. A failed insertion still cleans up its consumed
argument.

### Iteration order and views

Queue iteration is FIFO, and stack iteration is top-first. AsEnumerable captures
the same storage and count. Keep the owner alive and structurally unchanged while
using a view. Element edits stay visible, while structural mutation, moving, or
destruction invalidates the view.

Enumerator Current throws before the first successful MoveNext and after the end.
Noncopyable elements support direct borrowed access, Count, and Any. Query
operators returning elements by value can require copying when specialized, even
if a later terminal only counts them. See [queries](../../../docs/QUERIES.md).

## Packed bits

`BitSet` owns a fixed-length bit universe. Length is its bit count, while
CountSetBits counts true values. Its indexer reads or assigns bool values:

```csharp
using Standard;
using Standard::Collections;

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

### Equality and hashing

Equals compares Length and every logical bit. Different lengths return false.
GetHashCode includes Length and packed words. Padding remains zero. Like other
mutable hash keys, a stored BitSet must stay unchanged through aliases.

```csharp
using Standard;
using Standard::Collections;

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

Copying and Clone create independent storage. Allocation failure preserves the
source and an existing assignment target. Moving transfers its buffer.

### Set operations

| Method | Result |
| --- | --- |
| `HasAnySet` / `HasAllSet` | Tests packed occupancy. Empty input returns false/true |
| `Overlaps(other)` | Tests whether a bit is set in both |
| `IsSubsetOf(other)` / `IsSupersetOf(other)` | Tests set inclusion |
| `UnionWith` / `IntersectWith` | Mutates to union/intersection |
| `ExceptWith` / `SymmetricExceptWith` | Removes other's bits or toggles them |

The binary set operations require equal Length and throw before inspecting words
when lengths differ. Inclusion tests return true for two empty sets and for
self-comparison. Overlaps returns false for empty sets.

```cpp
using Standard;
using Standard::Collections;

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

### Shifting and ranges

Resize preserves the shared bit prefix, clears newly added bits, and discards a
removed tail. SetAll and Clear fill the current logical range.

ShiftLeft moves bits toward higher indices, and ShiftRight moves them lower.
Both preserve Length and fill vacated positions with zero. A count at least
Length clears the set. Zero changes nothing.

SetRange assigns a consecutive region, and FlipRange toggles one. They validate
the complete range before writing. A zero range is valid through Length.

### Iteration

Enumeration yields set-bit indices in ascending order. AsEnumerable captures
the word pointer and bit length. Existing bit edits can affect later reads.
An active cursor caches Current until MoveNext, and changes behind its position
never repeat indices. Keep storage alive and avoid resize, move, or destruction
while views are in use.

The public View constructor can borrow caller-owned words. Supply
`ceil(bitLength / 32)` words for a positive length. Null is valid for an empty view.

### Finding a matching bit

`FindIndex(value, start = 0)` returns the first matching logical bit at or after
start, or Length. It skips whole words and excludes unused final-word padding.
Start may equal Length. A larger start throws before inspecting words.

```cpp
using Standard;
using Standard::Collections;

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

Use FindIndex(false) for a free slot and the ordinary enumerator for all set bits.

## Priority queue

`PriorityQueue<T>` owns a min-heap. A negative comparison gives the left element
higher removal priority. Equal-priority order is unstable. Keep priorities and
comparison behavior consistent while stored:

```csharp
using Standard;
using Standard::Collections;

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

Enqueue, Dequeue, and TryDequeue use the queue ownership rules above. Top borrows
`ref immutable(T)`. Repeated removal produces priority order.

Insertion and removal compare the complete path before relocating existing
values. Comparator or allocation failure therefore preserves contents and heap
order. Comparators must leave the queue and its values unchanged.

ShrinkToFit reduces capacity while retaining the current heap. Successful
relocation invalidates element views. Allocation failure preserves them.

## Source selection

Ordinary Standard projects include these APIs. Selective builds add the relevant
container and its normal value, memory, and interface dependencies. Optional
operations are separate files:

| File | Operations |
| --- | --- |
| `Collections/vector_add_range.gf` | AddRange |
| `Collections/vector_insert_range.gf` | InsertRange, also using vector_add_range and span algorithms |
| `Collections/vector_resize.gf` | Resize overloads |
| `Memory/span_copy.gf` | CopyTo and TryCopyTo |
| `Memory/span_fill.gf` | Fill |
| `Collections/stable_sort.gf` | StableSort |

Container query adapters use `Queries/Enumerable.gf`. Include the query operator
sources that the program calls.
