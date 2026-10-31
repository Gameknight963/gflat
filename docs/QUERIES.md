# Enumeration and queries

`Vector`, `Deque`, `Queue`, `Stack`, `LinkedList`, `Span`, `IntRange`,
`FilteredIntRange`, `Repeated<T>`, `TextParts`, `TextLines`, `Utf8Scalars`, `Utf16Scalars`, and
`BitSet` provide an `AsEnumerable()` adapter
for lazy queries.
[`Repeated<T>`](REPEATED_VALUES.md) captures a value. Its query and
cursor copies use that value's ordinary ownership rules.
Import `Standard::Queries` to make the query extensions visible:

```csharp
using Standard::Text;
using Standard; using Standard::Queries; using Standard::Collections;

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
structural mutation while using a query. Element edits remain visible.
Its adapter currently requires a mutable owner. See [Deque views](DEQUE.md).

Queue queries preserve FIFO order, while stack queries visit the top first.
Their adapters likewise borrow captured storage metadata and require a mutable
owner. Keep that owner alive and avoid structural mutation while using the query.
Ordinary element edits remain visible. See [queue and stack views](../Standard/core/Collections/OWNING_COLLECTIONS.md#queue-and-stack).

Linked-list queries capture the first node and visit values from first to last.
Do not insert or remove nodes while using them. As with node handles, an owner
move preserves these views while the destination owner lives. See
[linked-list borrowing](LINKED_LIST.md#borrowing-and-iteration).

Integer ranges are different from container views: their adapters copy the small
range state rather than borrowing the original range variable. Both range
adapters accept immutable receivers. A filtered range remains lazy and can enter
the general query operators with `.AsEnumerable().Select(...)`. Constructing
that adapter does not evaluate its predicate or allocate storage. Each
enumeration starts again from the captured range, filter, skip, and take state.
Skipping filtered matches evaluates the predicate while consuming them, even
when the skip exhausts an earlier take limit. A subsequent `Take(0)` suppresses
that work, as with the general query operators.

Text and Unicode scalar adapters copy small views that borrow the caller's
storage. BitSet's adapter captures its packed storage and bit length, yielding
set-bit indices. These adapters do not copy their owners or extend a buffer's
lifetime. See [text parts](TEXT_PARTS.md),
[Unicode scalars](UTF8_SCALARS.md), and
[BitSet queries](../Standard/core/Collections/OWNING_COLLECTIONS.md#packed-bits) for their invalidation rules.

## Operations

| Operation | Behavior |
| --- | --- |
| `Where(predicate)` | Yield elements passing the predicate. Optional [indexed filtering](QUERY_INDEXED_WHERE.md) also supplies the upstream encounter index. |
| `Select(selector)` | Project elements when their `Current` value is read. Optional [indexed projection](QUERY_INDEXED_SELECT.md) supplies a zero-based `nuint` index. |
| `Reverse()` | Yield in reverse order, buffering the complete input on each iterator's first move. |
| `TakeLast(count)` | Yield the final at most count elements, buffering a bounded tail on each iterator's first move. |
| `SkipLast(count)` | Omit a trailing suffix using bounded streaming lookahead. |
| `Order()` / `Order(compare)` | Stably sort the complete input on each iterator's first move. |
| `Distinct()` | Yield distinct values in encounter order. Each enumeration owns a hash set. |
| `DistinctBy(selector)` | Yield the first whole value for each selected key, in encounter order. |
| `Zip(other, selector)` | Project pairs in order until either input ends. |
| `Concat(other)` | Yield the complete first query, then the second, with inline cursor state. |
| `Skip(count)` / `Take(count)` | Skip or yield at most `count` elements. Counts are `nuint`. |
| `TakeWhile(predicate)` | Yield the initial matching prefix. Stop permanently at the first false result. |
| `SkipWhile(predicate)` | Skip the initial matching prefix, then yield every remaining element without testing again. |
| `Any()` | Test for at least one element, stopping immediately. |
| `Any(predicate)` | Stop at the first element passing the predicate. False for an empty sequence. |
| `Contains(&key)` / `Contains(&key, equal)` | Search using default equality or an explicit borrowed-value comparer. Stop at the first match. |
| `SequenceEqual(other)` / `SequenceEqual(other, equal)` | Compare two sequences with the same element type in order. Stop at the first unequal pair or length mismatch. |
| `All(predicate)` | Stop at the first failure. True for an empty sequence. |
| `Count()` | Count elements without reading the terminal iterator's `Current`. Throw on count overflow. Upstream operators can still read their inputs. |
| `Count(predicate)` | Count matching elements by testing every value. Return zero for an empty sequence and throw on count overflow. |
| `Sum()` | Reduce elements implicitly convertible to `long`, checking each signed 64-bit addition. Zero for empty input. |
| `First()` / `Last()` / `Single()` | Return a value. Throw if empty. `Single` also rejects multiple elements. |
| `ElementAt(index)` | Return the zero-based element. Throw with code 3 if out of range. |
| `TryElementAt(index, ref result)` | Return the selected element through initialized output, or false with output unchanged if out of range. |
| `TryFirst(ref result)` | Return false without changing `result` if empty. |
| `TryLast(ref result)` | Return the last value, or false without changing `result` if empty. |
| `TrySingle(ref result)` | Return the only value, or false without changing `result` if empty or multiple. |
| `Aggregate(seed, function)` | Fold values into an accumulator, returning the seed if empty. |
| `Aggregate(function)` | Fold from the first value. Throws if empty. |
| `Min()` / `Max()` / `Min(compare)` / `Max(compare)` | Select the first least or greatest value. Throw if empty. |
| `MinBy(selector)` / `MaxBy(selector)` | Select the first element with the least or greatest key. Optional key comparer. Throw if empty. |
| `TryMinBy(selector, ref result)` / `TryMaxBy(selector, ref result)` | Select a keyed extremum in one pass, or false with unchanged output when empty. Optional key comparer. |
| `Append(value)` / `Prepend(value)` | Lazily add one captured value after or before the source. |
| `DefaultIfEmpty(value)` | Yield the source, or one captured value if the source is empty. |
| `ForEach(action)` | Visit every element. |
| `ToVector()` | Copy values into a new owning vector. |
| `ToHashSet()` | Copy distinct values into a new owning hash set. |
| `CountBy(selector)` | Materialize an owning hash map of selected-key counts. |
| `ToHashMap(keySelector)` / `ToHashMap(keySelector, valueSelector)` | Materialize an owning map. Reject duplicate selected keys. |
| `ToLookup(keySelector)` / `ToLookup(keySelector, valueSelector)` | Materialize an owning map of vectors, preserving encounter order within each group. |

See [sequence equality](QUERY_SEQUENCE_EQUAL.md) for paired
advancement order, equality contracts, and temporary element ownership.
The ref-output `Try` methods currently require writable element storage types.
See the [qualified-element limitation](QUERY_ELEMENT_AT.md).

## Evaluation

Operators are lazy until a terminal operation or `foreach` advances an iterator.
The individual guides describe consumption order, buffering, and value ownership:

- Combining sources: [Zip](QUERY_ZIP.md), [Concat](QUERY_CONCAT.md),
  [Append and Prepend](QUERY_ENDPOINT.md)
- Selecting elements: [ElementAt](QUERY_ELEMENT_AT.md), [extrema](QUERY_EXTREMA.md),
  [Distinct](QUERY_DISTINCT.md), [DistinctBy](QUERY_DISTINCT_BY.md),
  [DefaultIfEmpty](QUERY_DEFAULT_IF_EMPTY.md)
- Buffering: [TakeLast](QUERY_TAKE_LAST.md), [SkipLast](QUERY_SKIP_LAST.md),
  [Reverse](QUERY_REVERSE.md), [Order](QUERY_ORDER.md)
- Aggregation: [Sum](QUERY_SUM.md), [ToHashSet](QUERY_HASH_SET.md),
  [CountBy](QUERY_COUNT_BY.md), [ToHashMap](QUERY_TO_HASH_MAP.md),
  [ToLookup](QUERY_LOOKUP.md)
- Borrowed views: [span windows](SPAN_WINDOWS.md), [span chunks](SPAN_CHUNKS.md)

Each enumeration starts a fresh iterator, but does not rewind an external source.
For example, [FileLineSequence](../Standard/libc/README.md#reading-lines) continues from
its borrowed file's current position. Enumerating an ordered or reversed file
query again does not replay the previous buffer. A span starts again over its
captured view. Materialize values into an owning collection when replay is needed
independently of a consumable source.

Operator order affects which values are visited:

- `Take(3).Where(test)` examines at most three input elements
- `Where(test).Take(3)` looks for three matches
- `Take(0)` leaves its input unadvanced


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

### Projection and predicates

`Select` recomputes projected values. Reading its `Current` twice invokes
the selector twice. `Any()` and `Count()` need not invoke a selector at all. This
allows arbitrary result types without default construction or hidden storage
allocation. Put effects in `ForEach`, rather than relying on a selector running.

Optional indexed `Select` supplies a zero-based `nuint` encounter index. The
index advances on successful moves, not getter calls. Repeated reads project
the same index. It keeps ordinary `Select` copy, retry and uncached getter
semantics. See [indexed projection](QUERY_INDEXED_SELECT.md).

Optional indexed `Where` counts all successful upstream encounters, including
predicate rejections. Its predicate runs during `MoveNext`. Accepted `Current`
reads remain uncached and do not rerun it. See
[indexed filtering](QUERY_INDEXED_WHERE.md) for retry, index and
ownership contracts.

`Any(predicate)` and both `Contains` overloads read terminal `Current` once for
each element tested. They stop before reading a later element after a successful
match. `Count(predicate)` reads each terminal value once and scans the whole
sequence. Query membership borrows the search key but follows ordinary element-copy
semantics. See [query searches](QUERY_SEARCH.md).

### Allocation and callbacks

- Built-in source views, `Where`, `Select`, `Zip`, `Concat`, `Take`, `Skip`,
  `TakeWhile`, and `SkipWhile` store their internal state inline
- `Distinct` and `DistinctBy` grow an iterator-owned hash set as values arrive
- `Reverse` fills an iterator-owned vector on its first move
- `TakeLast` fills an iterator-owned bounded deque on its first move. A zero
  count skips upstream iteration
- `Order` buffers input on its first move and uses `StableSort`'s two index buffers

Custom sources, callbacks, element copy constructors, and materialization can
allocate independently of the operator's internal storage.

Callback signatures use ordinary
function pointers. Capturing closures are not implemented. For owning value
parameters such as `String`, use a named function: lambda parameters currently
must borrow those values through pointers.

## Ownership

Elements follow ordinary value semantics:

- Assigning a borrowed `String` to an owning local or passing it by value copies
  its bytes. `ToVector()` creates independently owned strings
- A borrowed `Current`, such as a span, file-line, or ordered cursor's getter,
  permits inspection through a reference
- Copying a raw pointer copies its address. Its pointee stays borrowed
- Managed pointer elements retain their collector's normal reference behavior

`ToVector()` requires an element type accepted by `Vector<T>`. An element type
with outer `readonly` or `immutable` qualification currently fails its writable
indexer check. Choose an ordinary stored value explicitly, for example
`.Select((readonly(int) value) => (int)value).ToVector()` for readonly integers.
The same restriction applies to buffering with query `Reverse()`, `Order()`, `TakeLast(count)`, or `SkipLast(count)`.
Managed pointer elements keep their normal collector behavior.

`Aggregate` owns its seed parameter. Passing a named seed normally copies it at
entry. Use `move seed` to transfer it instead, including a noncopyable owner such
as `Unique<T>`. Each iteration transfers the current accumulator to the callback,
then uses the callback's returned value as the next accumulator. A callback that
returns its owning parameter should return `move accumulator`. This avoids
copying the accumulator on every step. Source elements still follow ordinary
callback argument copying rules. An empty sequence returns the supplied seed.
Iterator or callback exceptions clean up the current accumulator and iterator.

Without a seed, `Aggregate(function)` starts with the first element and folds
the remaining elements from left to right. It throws `"Sequence is empty"` on
an empty sequence. One element needs no callback. Otherwise there is one
callback per remaining element. Each element's `Current` is read once. The
accumulator is transferred to the callback and back using the same ownership
rules as the seeded overload. The source's elements are read normally.

```csharp
using Standard; using Standard::Memory; using Standard::Queries;

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
using Standard; using Standard::Memory; using Standard::Queries;

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
unchanged. These errors still propagate normally. `TrySingle` stops after finding
a second element and does not read its `Current` value. Successful outputs follow
ordinary copying rules and own their independent resources, as with `Last` and
`Single`.

The output-parameter terminals currently have a compiler limitation for qualified
element types such as `readonly(int)`: their `ref T` destination becomes readonly
too, so assigning the answer is rejected. Value-returning terminals such as
`First()` and `ElementAt()` work for these views and can initialize a new local.

These output guarantees describe writes made by the terminal operation. An
iterator or callback can also change storage through other aliases. If `result`
borrows that storage, those changes remain visible. Use independent output
storage when iteration mutates its source, and keep the output reference valid
for the entire call.

Iterators are values. Their destructors and owned fields are cleaned up on
completion, early return, `break`, and exceptions. A custom iterator returned as
a raw pointer has ordinary borrowed-pointer semantics. Returning a pointer does
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
other small views. It must not retain a pointer to a temporary source object.
Iterators themselves may own resources and need not be copyable.

Query extension methods borrow their receiver. Composed operators explicitly
capture a copy of its source view through `SourceView()`, following ordinary
value semantics. A source or iterator can own backing storage and release it
during cleanup. Returning a raw pointer from a terminal
operation does not keep those source copies or iterators alive: a provider of
borrowed pointer elements must arrange for their storage to survive enumeration
and terminal cleanup. Built-in borrowed views satisfy this when the original
owner remains alive. Owning sources can also work, for example with independently
copied value outputs or shared backing owners whose storage remains alive.
There is no automatic lifetime tracking for raw pointer results.

## Select an element by its key

`MinBy(selector)` and `MaxBy(selector)` return the first element with the least
or greatest projected key.
An explicit comparer is accepted as `MinBy(selector, compare)` or
`MaxBy(selector, compare)`. The selector has type `K(T)*`. The comparer has type
`int(immutable(K)*, immutable(K)*)*`. Default comparison requires the exact
`IComparable<K>` contract and forwards through `Comparer<K>::Compare`.

```csharp
using Standard; using Standard::Memory; using Standard::Queries;

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
source element copies it. Passing that local to the by-value selector follows
ordinary callback argument copying rules too. A returned owning key is stored
as an ordinary value. A new winner moves both element and key into the retained
locals. Non-winning locals and replaced winners are cleaned up normally. The
result element is moved to the caller, and the retained key is destroyed.
The scan retains the best element and key. Iterators, element/key copies, and
callbacks can allocate or throw.

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

As for Min/Max, retained element and key locals must permit ordinary assignment.
Outer readonly/immutable types do not become writable automatically. Default
comparison preserves the key type's existing ordering, including its floating
NaN/signed-zero policy. Explicit comparers must define a consistent ordering.

Selective source users add Queries/enumerable_extrema_by.gf with the ordinary
sequence contracts. Default comparison additionally uses Collections/Comparer.gf and
IComparable.gf. Include the dependencies of the selected element, iterator,
callbacks, and exception runtime as well.


### Empty-safe keyed extrema

`TryMinBy(selector, ref result)` and `TryMaxBy(selector, ref result)` scan once
and return false for an empty sequence, leaving the initialized output unchanged.
Explicit comparison uses `(selector, compare, ref result)`. Like the throwing
forms, they select the first tied element, read each `Current` and select its key
once, and compare the current key with the retained best key.

```csharp
using Standard; using Standard::Memory; using Standard::Queries;

struct Ticket
{
    public int Priority;
    public int Id;
    public Ticket(int priority, int id) { Priority = priority; Id = id; }
}
int Priority(Ticket value) => value.Priority;

int main()
{
    Ticket[4] storage = [new Ticket(4, 0), new Ticket(1, 1),
        new Ticket(1, 2), new Ticket(3, 3)];
    Span<Ticket> values = new(storage, 4);
    Ticket best = new(9, 99);
    if (!values.AsEnumerable().TryMinBy(&Priority, ref best) || best.Id != 1) return 1;
    if (!values.AsEnumerable().TryMaxBy(&Priority, ref best) || best.Id != 0) return 2;
    Span<Ticket> empty = new();
    if (empty.AsEnumerable().TryMinBy(&Priority, ref best) || best.Id != 0) return 3;
    return 0;
}
```

The output is replaced only after the complete scan, following `TryLast`'s
ordinary move-assignment and destructor rules. Iterator, element/key copying,
selector and comparer failures propagate. They do not become false, and failures
before the final assignment preserve the old output. Source consumption and
callback effects are not rolled back. A separate `Any()` check is unnecessary,
which matters for stateful sources whose enumerations consume input.

Elements, keys and source views retain the ownership and borrowing rules above.
The loop retains the best element and key. Value copies and callbacks can
allocate. Ref-output overloads require writable element types.
