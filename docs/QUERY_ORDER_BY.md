# Ordering query elements by key

`OrderBy(selector)` yields elements in ascending key order.
`OrderByDescending(selector)` yields them in descending key order. Both preserve
input order among equal keys, including descending ties.

```csharp
using Standard::Collections;
using Standard::Memory;
using Standard::Queries;

int Priority(int value) => value / 10;

int main()
{
    int[4] input = [32, 11, 21, 12];
    var source = new Span<int>(&input[0], 4).AsEnumerable();
    Vector<int> ascending = source.OrderBy(Priority).ToVector();
    Vector<int> descending = source.OrderByDescending(Priority).ToVector();
    if (ascending[0] != 11 || ascending[1] != 12
        || ascending[2] != 21 || ascending[3] != 32) return 1;
    if (descending[0] != 32 || descending[1] != 21
        || descending[2] != 11 || descending[3] != 12) return 2;
    return 0;
}
```

## Key comparison

The default overload requires `K : IComparable<K>`. To choose another ordering,
pass `int(immutable(K)*, immutable(K)*)* compare` as the second argument.
Its result is negative, zero, or positive according to key ordering. Descending
ordering reverses the comparison arguments, so comparers may return any signed
integer, including the minimum `int`.

Freestanding forms accept the query first, for example
`OrderBy(source, Priority)` or `OrderByDescending(source, Priority, compare)`.
Type arguments can be inferred.

These operators establish a primary ordering. Calling `OrderBy` again creates
a new primary ordering. Secondary-key `ThenBy` operations are not provided yet.

## Evaluation and ownership

Creating a query or its cursor does not enumerate the source. On the first
`MoveNext`, the cursor copies the complete finite input into an owning buffer
and closes its source iterator. It then computes one key per buffered element,
in input order, and sorts indices using those cached keys. Empty inputs call
neither the selector nor the comparer. `Take(0)` does not start the input.

`T` must be copyable because input snapshots and selector arguments use value
semantics. Selected keys are moved into temporary storage and can be noncopyable.
They are destroyed before the first result is yielded. Keep comparison ordering
consistent during each sort. Callbacks must not invalidate storage they borrow.

`Current` borrows an immutable element from the cursor's buffer. Repeated reads
perform no key selection or comparison. Keep the cursor alive and avoid advancing
it to completion while using that reference. Copy a value to retain it separately.
An owning `String` copy has independent storage. Pointer elements keep their
ordinary pointee lifetime requirements.

Each cursor builds its own snapshot on its first move. Re-enumeration observes
changes to the source, subject to the original source view's lifetime and range.
The source itself is not reordered.

## Cost and errors

Ordering takes O(n log n) comparisons and O(n) additional storage. The cursor
retains the element buffer and sorted indices while yielding. Cached keys and
merge scratch are released before yielding. At completion it destroys buffered
elements and retains empty buffer capacity until the cursor is destroyed.
Breaking iteration releases storage when its cursor leaves scope.

Allocation, source, copy, selector, and comparison errors propagate. A failed
cursor destroys its buffered elements and keys and stays finished. `Current`
throws before the first successful move, after completion, or after failure.

Selective builds load `Queries/OrderBySource.gf` and
`Queries/enumerable_order_by.gf`, plus the normal Enumerable, Vector, and Span
sources. Default comparisons also need `Collections/Comparer.gf` and the key's
comparison implementation.
