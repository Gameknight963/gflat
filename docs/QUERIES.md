# Enumeration and queries

Import `Standard::Queries` and use `AsEnumerable()` to build a query over a
collection or view:

```csharp
using Standard::Text;
using Standard;
using Standard::Queries;
using Standard::Collections;

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

## Sources and borrowing

A query captures source state when constructed. Keep borrowed storage alive until
the query and its iterators finish.

| Source | Captured state and invalidation |
| --- | --- |
| Vector | Element pointer and length. Growth, removal, and clearing can invalidate the viewed range |
| Span | Borrowed pointer and length. Keep the underlying storage and elements alive |
| Queue, Stack, and Deque | Storage and cursor metadata. Structural mutation invalidates the view |
| LinkedList | First node. Node insertion/removal invalidates iteration. Moving the owner preserves node addresses |
| BitSet | Packed storage and bit length. Resizing or destruction invalidates the view |
| Text and Unicode views | Borrowed source bytes |
| IntRange and FilteredIntRange | Copied range and filter state |
| Repeated<T> | A captured value with ordinary copy/ownership rules |
| FileLineSequence | A shared file cursor and an iterator-owned current-line buffer |

Moving an owning container transfers its allocation to the destination. Keep
that owner alive while using views into the allocation.

Edits to existing collection elements are visible through a valid view. Each
iterator starts from its captured state. A file sequence starts from the current
file position, so re-enumeration consumes further input. Use `ToVector()` when
values must be replayed independently of a consumable source.

`numbers.AsReadOnly().AsEnumerable()` preserves readonly element qualification.
For example, its predicate can accept `.Where((readonly(int) n) => n > 0)`.

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

### Evaluation order

- `Take(3).Where(test)` examines at most three input elements
- `Where(test).Take(3)` searches for three matches
- `Take(0)` constructs its input iterator, then performs no moves
- `TakeWhile` consumes the first rejected element and ends
- `SkipWhile` yields the first rejected element, then the remaining suffix

Construction of a custom iterator can throw even for `Take(0)`.

### Projection and predicates

`Select` invokes its selector whenever `Current` is read. Reading it twice
projects twice. `Any()` and `Count()` can finish without reading projected values.
Use `ForEach` for effects that must happen once per visited value.

Indexed `Select` supplies the successful move's encounter index. Indexed `Where`
counts every upstream encounter, including rejected values. See
[indexed projection](QUERY_INDEXED_SELECT.md) and
[indexed filtering](QUERY_INDEXED_WHERE.md).

`Any(predicate)`, `Contains`, and `Count(predicate)` read each tested terminal
value once. `Any` and `Contains` stop at the first match. See
[query searches](QUERY_SEARCH.md) for comparer and key rules.

### Allocation and callbacks

| Operators | Internal storage |
| --- | --- |
| `Where`, `Select`, `Zip`, `Concat`, `Take`, `Skip`, `TakeWhile`, `SkipWhile` | Inline source and cursor state |
| `Distinct`, `DistinctBy` | A hash set grown during iteration |
| `Reverse` | A vector filled on the first move |
| `TakeLast` | A bounded deque filled on the first move |
| `SkipLast` | A bounded lookahead buffer |
| `Order` | Buffered input plus StableSort's two index buffers |

The iterator owns its buffers. Copies of elements, custom sources, and callbacks
have their own allocation behavior.

Callbacks use function pointers and noncapturing lambdas. For an owning
by-value argument such as String, use a named function. Lambda parameters
currently require pointer borrowing for those values.

## Ownership

`ToVector()` copies elements into an owning vector. Reading a borrowed String
into a local also creates an independent copy. Inspect through `ref Current`
when the source exposes a borrowed getter. Raw pointer elements remain borrowed,
and managed pointers retain their collector behavior.

Iterators clean up owned fields on completion, `break`, return, and exceptions.
A raw pointer returned from a query remains valid only as long as its backing
owner. In particular, a pointer into an iterator-owned buffer expires when that
iterator is destroyed.

### Qualified elements

Buffering and materialization currently require mutable stored element types.
For a readonly integer source, select an ordinary stored value explicitly:
`.Select((readonly(int) value) => (int)value).ToVector()`.
This restriction also applies to `Reverse`, `Order`, `TakeLast`, and `SkipLast`.

Ref-output terminals require writable destinations. Value-returning terminals
such as `First()` and `ElementAt()` can initialize a local from a qualified view.
See [qualified outputs](QUERY_ELEMENT_AT.md).

## Aggregate

`Aggregate(function)` folds left from the first value and throws
`Exception("Sequence is empty")` for empty input:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int Subtract(int accumulated, int next) => accumulated - next;

int main()
{
    int[3] data = [12, 3, 2];
    Span<int> values = new(data, 3);
    // Left fold: (12 - 3) - 2.
    return values.AsEnumerable().Aggregate(Subtract) - 7;
}
```

`Aggregate(seed, function)` starts from the supplied seed and returns it for an
empty sequence. Each callback receives ownership of the current accumulator and
returns the next one. Move an owning seed to transfer it into the fold:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

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

A callback returning its owning accumulator should use `return move accumulator`.
Source elements retain their normal copying rules.

## Ref-output terminals

`TryFirst`, `TryLast`, `TrySingle`, and `TryElementAt` return false when their
selection fails and leave the output unchanged. They assign only after finding
the result. Iterator, callback, and element-copy errors propagate before that
assignment. A second element makes `TrySingle` fail without reading its Current.

Keep output separate from storage changed by iteration or callbacks. Writes
through other aliases remain visible even when the terminal leaves its output
unchanged.

## Custom sequences and optional interfaces

`foreach` accepts `GetEnumerator()`, `MoveNext()`, and `Current` by pattern.
`IEnumerable<T, Iterator>` requires the concrete iterator return type, and
`IEnumerator<T>` describes `MoveNext()` and a value-returning Current getter.
Borrowing iterators can expose `ref Current` through the pattern.

Construct `Enumerable<T, Source, Iterator>` for a custom query source. Source must
be copyable because composition captures a source view through `SourceView()`.
Extension methods borrow their receiver before making that explicit capture.
Iterators can own resources and be noncopyable.

Use source state that outlives every iterator and returned borrow. A view pointing
to a temporary source object becomes invalid when that object dies. An owning
source must arrange for pointer results to survive iterator and source-copy
cleanup if callers retain them.

## Select an element by its key

`MinBy(selector)` and `MaxBy(selector)` return the first element with the least
or greatest key. Add an explicit comparer as `(selector, compare)`:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

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

The selector has type `K(T)*`, and the comparer has type
`int(immutable(K)*, immutable(K)*)*`. Default comparison uses `IComparable<K>`
through `Comparer<K>::Compare`.

Each element and key is read once. For N nonempty elements there are N selector
calls and N-1 comparisons, with the current key first and retained best key
second. Equal keys keep the first element. Empty input throws
`Exception("Sequence is empty")`.

The scan retains one best element and key. Replacing the best value moves the new
winner and destroys the old one. The final element moves to the caller, and its
retained key is destroyed. Types must support the ordinary assignments used by
these locals.

Comparer arguments are borrows for the call. A pointer-valued key must stay valid
through the scan. Returning an address inside a by-value selector parameter
would leave the key dangling when that callback returns.

### Empty-safe keyed extrema

`TryMinBy(selector, ref result)` and `TryMaxBy(selector, ref result)` return false
for empty input. Explicit comparison uses `(selector, compare, ref result)`:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

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

Output is replaced after the scan completes. Iterator, copy, selector, and
comparer errors leave it unchanged and propagate. Source consumption and other
callback effects remain. These overloads share the writable-output restriction
above.

## Further references

- [Zip](QUERY_ZIP.md) and [Concat](QUERY_CONCAT.md) for paired advancement and captures
- [Distinct](QUERY_DISTINCT.md), [DistinctBy](QUERY_DISTINCT_BY.md), and
  [Order](QUERY_ORDER.md) for buffered ownership
- [TakeLast](QUERY_TAKE_LAST.md), [SkipLast](QUERY_SKIP_LAST.md), and
  [Reverse](QUERY_REVERSE.md) for tail and replay behavior
- [Min and Max](QUERY_EXTREMA.md) for ordering and ties
- [SequenceEqual](QUERY_SEQUENCE_EQUAL.md) for paired equality
- [Append and Prepend](QUERY_ENDPOINT.md) and [DefaultIfEmpty](QUERY_DEFAULT_IF_EMPTY.md)
  for captured values
- [Sum](QUERY_SUM.md), [CountBy](QUERY_COUNT_BY.md), [ToHashMap](QUERY_TO_HASH_MAP.md),
  and [ToLookup](QUERY_LOOKUP.md) for aggregates
