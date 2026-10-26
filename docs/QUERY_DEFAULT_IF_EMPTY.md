# Default value for an empty query

`DefaultIfEmpty(value)` yields the source unchanged when it has an element. If
its first `MoveNext()` reports no elements, the query yields its captured value
once. It creates a lazy, repeatable query and does not change the source.

```csharp
using std; using std::Memory; using std::Queries;
int main()
{
    Span<int> empty = new();
    var query = empty.AsEnumerable().DefaultIfEmpty(42);
    int count = 0;
    foreach (int value in query)
    {
        if (value != 42) return 1;
        count++;
    }
    return count - 1;
}
```

The overload requires an explicit value with the sequence's existing `T` type.
It composes with `Where`, `Take`, `Append` and other existing operators. Filtering
the source to empty selects the fallback; filtering the result can remove it.
A nonempty or infinite source never emits the fallback.

Creating the query captures a normal source view and the fallback under ordinary
copy/move rules. A named owning fallback is copied; `DefaultIfEmpty(move value)`
transfers it. Reusable queries require a copyable captured element. Moving a
noncopyable owner into the factory does not make repeatable enumeration possible.
There is no implicit borrow of the fallback variable or lifetime extension for
underlying borrowed source storage.

`GetEnumerator()` constructs the underlying iterator, then copies the fallback
into the wrapper iterator. It does not call underlying `MoveNext` or `Current`.
The source may turn out to be nonempty, but the fallback is still copied and
owned. `Take(0)` can also construct/copy source and fallback state before any
advance. These copies may allocate or throw according to their types. If fallback
copying fails after underlying iterator construction, ordinary cleanup destroys
that iterator and any partially initialized fields.

The first move examines only the source's `MoveNext`. True selects its current
source value; false selects the fallback once without reading source `Current`.
After source iteration has begun, its first false move finishes the wrapper with
no additional fallback. After the fallback, the next move finishes without
advancing the source again. Exhaustion is sticky.

`Current` returns an ordinary `T` value. For source elements it delegates to the
underlying `Current`, preserving effects such as repeated selector evaluation.
For the fallback it copies the captured value on each read. It throws before the
first successful move, after exhaustion, or after a failed move. A `Current`
exception does not advance or finish the wrapper, so another read can retry.
An underlying `MoveNext` exception propagates and permanently finishes this
wrapper iterator; a fresh enumeration can retry the source.

Each iterator owns its underlying cursor and its separate fallback copy. They
remain alive until iterator cleanup, including after a nonempty source, early
exit, or exhaustion. Source view and cursor borrowing rules still apply; owning
cursor implementations can outlive the query, while borrowed source storage
must remain valid. Query copying and source `SourceView()` follow the normal
ownership rules of their source type.

The wrapper itself uses constant inline state, with no type erasure, direct heap
allocation or direct GC calls. Owning element/source copies and underlying
iterators may allocate or throw. Raw pointers copy addresses with their normal
pointee rules; managed references retain their ordinary shared targets and roots.
There is no allocation-free promise for an arbitrary owning `T`.

As with `Append` and `Prepend`, capturing moves a value parameter of existing `T`.
Outer `readonly` or `immutable` element types can require an explicit projection
to an ordinary value before capture. The implementation follows the existing
mutable generic observer/getter pattern to preserve ordinary pointer/reference
copies; it does not add qualifier conversions or new compiler rules.

Selective users include `Queries/DefaultIfEmptySource.gf` and
`Queries/enumerable_default_if_empty.gf` with the normal IEnumerable,
IEnumerator and Enumerable contracts, plus the other operators they call.
No Vector, String, libc or collector dependency is introduced by this operator;
dependencies of its element, iterator and exception runtime still apply.
