# Enumeration and queries

[`CountBy(selector)`](../../../docs/QUERY_COUNT_BY.md) materializes selected-key
counts into an owning hash map without storing each group of elements.
[`ToHashMap`](../../../docs/QUERY_TO_HASH_MAP.md) stores each original element or
a selected value under its selected key, rejecting duplicate keys.
[`ToLookup`](../../../docs/QUERY_LOOKUP.md) instead groups equal keys into owning
vectors, preserving encounter order within each group.

`Vector`, `Deque`, `Queue`, `Stack`, `LinkedList`, `Span`, `IntRange`,
`FilteredIntRange`, `Repeated<T>`, `TextParts`, `TextLines`, `Utf8Scalars`, `Utf16Scalars`, and
`BitSet` provide an `AsEnumerable()` adapter
for lazy queries.
[`Repeated<T>`](../../../docs/REPEATED_VALUES.md) captures a value; its query and
cursor copies use that value's ordinary ownership rules.
Import `std` to make the query extensions visible:

```csharp
using std;

int main()
{
    Vector<int> numbers = new Vector<int>();
    for (int i = 1; i <= 10; i++) numbers.Add(i);

    Vector<int>* borrowed = &numbers;
    var query = borrowed.AsEnumerable()
        .Where((int n) => n % 2 == 0)
        .Select((int n) => n * n)
        .Take(3);

    foreach (int number in query)
    {
        String line = s$"{number}";
        Console::WriteLine(ref line);
    }
    return 0;
}
```

The query stores a span of the vector's elements. It copies neither the vector
nor its elements when the query is built. Keep the vector alive and do not grow,
clear, remove from, or move it while using the view. The length is captured when
the view is created. Editing existing elements is visible on the next read.
For a readonly view use `numbers.AsReadOnly().AsEnumerable()`.
Its callback parameter types retain that qualification, for example
`.Where((readonly(int) n) => n > 0)`.

Deque queries capture the ring buffer's pointer, head, capacity, and count,
rather than requiring contiguous storage. Keep the owner alive and avoid
structural mutation while using a query; element edits remain visible.
Its adapter currently requires a mutable owner. See [Deque views](../../../docs/DEQUE.md).

Queue queries preserve FIFO order, while stack queries visit the top first.
Their adapters likewise borrow captured storage metadata and require a mutable
owner. Keep that owner alive and avoid structural mutation while using the query;
ordinary element edits remain visible. See [queue and stack views](OWNING_COLLECTIONS.md#queue-and-stack).

Linked-list queries capture the first node and visit values from first to last.
Do not insert or remove nodes while using them. As with node handles, an owner
move preserves these views while the destination owner lives. See
[linked-list borrowing](../../../docs/LINKED_LIST.md#borrowing-and-iteration).

Integer ranges are different from container views: their adapters copy the small
range state rather than borrowing the original range variable. Both range
adapters accept immutable receivers. A filtered range remains lazy and can enter
the general query operators with `.AsEnumerable().Select(...)`; constructing
that adapter does not evaluate its predicate or allocate storage. Each
enumeration starts again from the captured range, filter, skip, and take state.
Skipping filtered matches evaluates the predicate while consuming them, even
when the skip exhausts an earlier take limit. A subsequent `Take(0)` suppresses
that work, as with the general query operators.

Text and Unicode scalar adapters copy small views that borrow the caller's
storage. BitSet's adapter captures its packed storage and bit length, yielding
set-bit indices. These adapters do not copy their owners or extend a buffer's
lifetime. See [text parts](../../../docs/TEXT_PARTS.md),
[Unicode scalars](../../../docs/UTF8_SCALARS.md), and
[BitSet queries](OWNING_COLLECTIONS.md#packed-bits) for their invalidation rules.

## Operations

| Operation | Behavior |
| --- | --- |
| `Where(predicate)` | Yield elements passing the predicate. |
| `Select(selector)` | Project elements when their `Current` value is read. |
| `Reverse()` | Yield in reverse order, buffering the complete input on each iterator's first move. |
| `Distinct()` | Yield distinct values in encounter order; each enumeration owns a hash set. |
| `Zip(other, selector)` | Project pairs in order until either input ends. |
| `Skip(count)` / `Take(count)` | Skip or yield at most `count` elements; counts are `nuint`. |
| `TakeWhile(predicate)` | Yield the initial matching prefix; stop permanently at the first false result. |
| `SkipWhile(predicate)` | Skip the initial matching prefix, then yield every remaining element without testing again. |
| `Any()` | Test for at least one element, stopping immediately. |
| `Any(predicate)` | Stop at the first element passing the predicate; false for an empty sequence. |
| `Contains(&key)` / `Contains(&key, equal)` | Search using default equality or an explicit borrowed-value comparer; stop at the first match. |
| `SequenceEqual(other)` / `SequenceEqual(other, equal)` | Compare two sequences with the same element type in order; stop at the first unequal pair or length mismatch. |
| `All(predicate)` | Stop at the first failure; true for an empty sequence. |
| `Count()` | Count elements without reading the terminal iterator's `Current`; throw on count overflow. Upstream operators can still read their inputs. |
| `Count(predicate)` | Count matching elements by testing every value; return zero for an empty sequence and throw on count overflow. |
| `Sum()` | Reduce elements implicitly convertible to `long`, checking each signed 64-bit addition; zero for empty input. |
| `First()` / `Last()` / `Single()` | Return a value; throw if empty. `Single` also rejects multiple elements. |
| `ElementAt(index)` | Return the zero-based element; throw with code 3 if out of range. |
| `TryElementAt(index, ref result)` | Return the selected element through initialized output, or false with output unchanged if out of range. |
| `TryFirst(ref result)` | Return false without changing `result` if empty. |
| `TryLast(ref result)` | Return the last value, or false without changing `result` if empty. |
| `TrySingle(ref result)` | Return the only value, or false without changing `result` if empty or multiple. |
| `Aggregate(seed, function)` | Fold values into an accumulator, returning the seed if empty. |
| `Aggregate(function)` | Fold from the first value; throws if empty. |
| `Min()` / `Max()` / `Min(compare)` / `Max(compare)` | Select the first least or greatest value; throw if empty. |
| `MinBy(selector)` / `MaxBy(selector)` | Select the first element with the least or greatest key; optional key comparer; throw if empty. |
| `Append(value)` / `Prepend(value)` | Lazily add one captured value after or before the source. |
| `DefaultIfEmpty(value)` | Yield the source, or one captured value if the source is empty. |
| `ForEach(action)` | Visit every element. |
| `ToVector()` | Copy values into a new owning vector. |
| `ToHashSet()` | Copy distinct values into a new owning hash set. |

See [sequence equality](../../../docs/QUERY_SEQUENCE_EQUAL.md) for paired
advancement order, equality contracts, and temporary element ownership.
The ref-output `Try` methods currently require writable element storage types;
see the [qualified-element limitation](../../../docs/QUERY_ELEMENT_AT.md).

Operators are lazy until a terminal operation or `foreach` advances an iterator.
For paired queries and their iteration/ownership rules, see [Zip](../../../docs/QUERY_ZIP.md).
For positional access and projection behavior, see [ElementAt](../../../docs/QUERY_ELEMENT_AT.md).
For owning distinct results, see [ToHashSet](../../../docs/QUERY_HASH_SET.md).
For lazy distinct values in encounter order, see [Distinct](../../../docs/QUERY_DISTINCT.md).
For deferred full-input buffering in reverse order, see [Reverse](../../../docs/QUERY_REVERSE.md).
For comparison, ties, and value ownership in `Min` and `Max`, see [query extrema](../../../docs/QUERY_EXTREMA.md).
For captured values and iterator behavior in `Append` and `Prepend`, see [query endpoints](../../../docs/QUERY_ENDPOINT.md).
For an explicit fallback and its capture lifetime, see [DefaultIfEmpty](../../../docs/QUERY_DEFAULT_IF_EMPTY.md).
For widened integer totals, readonly views, and overflow behavior, see [Sum](../../../docs/QUERY_SUM.md).
For overlapping borrowed views of contiguous data, see [span windows](../../../docs/SPAN_WINDOWS.md).
For nonoverlapping views that retain a shorter final segment, see
[span chunks](../../../docs/SPAN_CHUNKS.md).
Each enumeration starts a fresh iterator. Operator order matters:
`Take(3).Where(test)` examines at most three input elements;
`Where(test).Take(3)` looks for three matches.
`Take(0)` never advances its input.
Starting that enumeration still constructs the input iterator, so a custom
`GetEnumerator()` or iterator constructor can allocate or throw even for
`Take(0)`. Any successfully constructed iterator is destroyed when enumeration
ends, including before the first element.

`TakeWhile` examines but does not yield the first element rejected by its
predicate. `SkipWhile` yields that first rejected element. Both start fresh on
each enumeration and never advance their input again after they finish. Their
`Current` getters delegate to the input without caching: when used after
`Select`, testing a predicate and then reading the accepted value can invoke
the selector separately, as with `Where`.

`Select` does not cache projected values. Reading its `Current` twice invokes
the selector twice. `Any()` and `Count()` need not invoke a selector at all. This
allows arbitrary result types without default construction or hidden storage
allocation. Put effects in `ForEach`, rather than relying on a selector running.

`Any(predicate)` and both `Contains` overloads read terminal `Current` once for
each element tested. They stop before reading a later element after a successful
match. `Count(predicate)` reads each terminal value once and scans the whole
sequence. Query membership borrows the search key but follows ordinary element-copy
semantics; see [query searches](../../../docs/QUERY_SEARCH.md).

The built-in source views allocate no storage. `Where`, `Select`, `Zip`, and the
skip/take operators allocate no internal storage. `Distinct` allocates a hash set
as values are encountered; its iterator owns that storage. `Reverse` buffers all
input values in a vector on its first move and retains that buffer until its
iterator is destroyed. A callback, a custom
source or element's copy constructor, or materializing a collection can allocate.
Callback signatures use ordinary
function pointers; capturing closures are not implemented. For owning value
parameters such as `String`, use a named function: lambda parameters currently
must borrow those values through pointers.

## Ownership

Elements follow ordinary value semantics. Reading a `String` element copies it;
`ToVector()` creates independent owning strings. Reading a pointer copies its
address. Pointer elements do not confer ownership: the query and the resulting
vector do **not** delete a pointee merely because they contain its address.

`ToVector()` requires an element type accepted by `Vector<T>`. An element type
with outer `readonly` or `immutable` qualification currently fails its writable
indexer check. Choose an ordinary stored value explicitly, for example
`.Select((readonly(int) value) => (int)value).ToVector()` for readonly integers.
The same restriction applies to buffering with query `Reverse()`.
Managed pointer elements keep their normal collector behavior.

`Aggregate` owns its seed parameter. Passing a named seed normally copies it at
entry; use `move seed` to transfer it instead, including a noncopyable owner such
as `Unique<T>`. Each iteration transfers the current accumulator to the callback,
then uses the callback's returned value as the next accumulator. A callback that
returns its owning parameter should return `move accumulator`. This avoids
copying the accumulator on every step; source elements still follow ordinary
callback argument copying rules. An empty sequence returns the supplied seed.
Iterator or callback exceptions clean up the current accumulator and iterator.

Without a seed, `Aggregate(function)` starts with the first element and folds
the remaining elements from left to right. It throws `"Sequence is empty"` on
an empty sequence. One element needs no callback; otherwise there is one
callback per remaining element. Each element's `Current` is read once. The
accumulator is transferred to the callback and back using the same ownership
rules as the seeded overload; the source's elements are read normally.

```csharp
using std;

int Subtract(int accumulated, int next) => accumulated - next;

int main()
{
    int[3] data = [12, 3, 2];
    Span<int> values = new(data, 3);
    // Left fold: (12 - 3) - 2.
    return values.AsEnumerable().Aggregate(Subtract) - 7;
}
```

For example, a fold can update one uniquely owned integer without reallocating it:

```csharp
using std;

Unique<int> Add(Unique<int> accumulator, int value)
{
    accumulator.Value += value;
    return move accumulator;
}

int main()
{
    int[3] data = [1, 2, 3];
    Span<int> values = new(data, 3);
    Unique<int> seed = new(new* int(10));
    Unique<int> total = values.AsEnumerable().Aggregate(move seed, Add);
    // seed is consumed; total now owns the same integer, containing 16.
    if (total.Value != 16) return 1;
    return 0;
}
```

`TryLast` and `TrySingle` assign their output only after iteration establishes
the answer. A throwing iterator, callback, or element copy leaves that output
unchanged; these errors still propagate normally. `TrySingle` stops after finding
a second element and does not read its `Current` value. Successful outputs follow
ordinary copying rules and own their independent resources, as with `Last` and
`Single`.

The output-parameter terminals currently have a compiler limitation for qualified
element types such as `readonly(int)`: their `ref T` destination becomes readonly
too, so assigning the answer is rejected. Value-returning terminals such as
`First()` and `ElementAt()` work for these views and can initialize a new local.

These output guarantees describe writes made by the terminal operation. An
iterator or callback can also change storage through other aliases; if `result`
borrows that storage, those changes remain visible. Use independent output
storage when iteration mutates its source, and keep the output reference valid
for the entire call.

Iterators are values. Their destructors and owned fields are cleaned up on
completion, early return, `break`, and exceptions. A custom iterator returned as
a raw pointer has ordinary borrowed-pointer semantics; returning a pointer does
not make the query responsible for deleting it.

## Custom sequences and optional interfaces

`IEnumerable<T, Iterator>` requires `GetEnumerator()` to return the specified
concrete iterator type. Keeping this second type parameter avoids requiring a
heap-allocated interface iterator. The value-producing `IEnumerator<T>` contract
requires `bool MoveNext()` and a `T Current` getter. Borrowing iterators, such as
`Span<T>::Enumerator`, can expose `ref Current` and use the pattern directly.

`foreach` continues to accept the pattern without either interface.
For custom queries, construct `Enumerable<T, Source, Iterator>` with a source
whose `GetEnumerator()` creates an iterator. The source must be copyable: it is
copied into composed views. Usually it should contain borrowed pointers or
other small views; it must not retain a pointer to a temporary source object.
Iterators themselves may own resources and need not be copyable.

Query extensions currently take their receiver by value, so custom source
copies follow ordinary value semantics. A source or iterator may own backing
storage and release it during cleanup. Returning a raw pointer from a terminal
operation does not keep those source copies or iterators alive: a provider of
borrowed pointer elements must arrange for their storage to survive enumeration
and terminal cleanup. Built-in borrowed views satisfy this when the original
owner remains alive. Owning sources can also work, for example with independently
copied value outputs or shared backing owners whose storage remains alive.
There is no automatic lifetime tracking for raw pointer results.

## Select an element by its key

`MinBy(selector)` and `MaxBy(selector)` select the first element with the least
or greatest projected key. They return the original element type, not the key.
An explicit comparer is accepted as `MinBy(selector, compare)` or
`MaxBy(selector, compare)`. The selector has type `K(T)*`; the comparer has type
`int(immutable(K)*, immutable(K)*)*`. Default comparison requires the exact
`IComparable<K>` contract and forwards through `Comparer<K>::Compare`.

```csharp
using std;

long Magnitude(int value)
{
    if (value < 0) return -(long)value;
    return (long)value;
}

int main()
{
    int[4] data = [-7, 3, -3, 5];
    Span<int> values = new(data, 4);
    if (values.AsEnumerable().MinBy(Magnitude) != 3) return 1;
    if (values.AsEnumerable().MaxBy(Magnitude) != -7) return 2;
    return 0;
}
```

The source is enumerated once. Each element's `Current` is read once, then
its key is computed once. For N nonempty elements there are N selector calls
and N-1 comparer calls. Comparisons receive the current key first and the best
key second. Equal keys retain the first element. A singleton still computes its
key but does not compare. An empty source throws `Exception("Sequence is empty")`
without reading Current or invoking either callback.

Elements and keys follow ordinary value semantics. Reading a borrowed owning
source element copies it; passing that local to the by-value selector follows
ordinary callback argument copying rules too. A returned owning key is stored
as an ordinary value. A new winner moves both element and key into the retained
locals. Non-winning locals and replaced winners are cleaned up normally. The
result element is moved to the caller, and the retained key is destroyed.
No sequence buffer, sorting, interface erasure, or managed allocation is added.
Iterator, element/key copies, and user callbacks can allocate or throw.

Callbacks must not invalidate iteration through another alias. A comparer may
borrow its two key arguments only during the call and must not retain those
addresses or mutate the keys. A key containing raw pointers must keep its
pointees valid throughout the scan: returning an address into a selector's
by-value parameter does not extend that parameter's lifetime. Pointer elements
and keys do not acquire ownership or lifetime tracking.

Exceptions propagate with ordinary cleanup of the iterator and initialized
current/best elements and keys. An assignment target in the caller is replaced
only after the terminal returns its completed result. Callback side effects
through other aliases are outside that guarantee.

As for Min/Max, retained element and key locals must permit ordinary assignment;
outer readonly/immutable types do not become writable automatically. Default
comparison preserves the key type's existing ordering, including its floating
NaN/signed-zero policy. Explicit comparers must define a consistent ordering.

Selective source users add Collections/EnumerableExtremaBy.gf with the ordinary
sequence contracts. Default comparison additionally uses Comparer.gf and
IComparable.gf. No Vector, String, or direct libc dependency is introduced by
the scan itself; selected element, iterator, callback, and exception dependencies
still apply.
