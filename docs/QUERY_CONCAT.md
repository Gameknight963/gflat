# Concatenating queries

`first.Concat(second)` yields every element of the first query, then every element
of the second. Both queries have the same element type; their source and iterator
types may differ. It can join several input batches without creating an
intermediate vector.

```cpp
using std; using std::Memory; using std::Queries;

int main()
{
    int[2] prefix = [1, 2];
    Span<int> first = new(prefix, 2);
    IntRange second = new(3, 3);
    int sum = 0;
    foreach (int value in first.AsEnumerable().Concat(second.AsEnumerable()))
        sum += value;
    if (sum != 15) return 1;
    return 0;
}
```

Constructing the query copies its source views without advancing either source.
Built-in borrowed views and Concat's own state allocate no storage. Ordinary
source copies, custom iterator construction, or element copies can allocate or
throw. Borrowed views retain their backing-owner lifetime and stability
requirements; copying a query does not copy the elements in a borrowed span.
Captured source views must support normal value copying, as with Zip. The
built-in span and file-line views copy only their ordinary borrowed-view fields.

`GetEnumerator()` constructs the left iterator, then the right iterator. Both
exist even when one input is empty or a later `Take(0)` never advances them.
Fresh iterator owners are moved into inline fields; no default iterator
constructor, heap cursor, or GC allocation is needed. If constructing the right
iterator throws, the completed left iterator is destroyed normally.

`MoveNext()` advances the left iterator until it returns false, then advances the
right iterator. It never advances the right input before the left ends. A single
move can encounter left EOF and yield the first right element. After right EOF,
further moves advance neither input. A failed move leaves this wrapper finished:
further moves return false and `Current` is invalid. The original error propagates
without rolling back provider effects.

`Current` returns an ordinary value from the active input and is not cached.
Reading it twice reads the active input twice, so a preceding `Select` can run
its selector twice. Moving does not itself read `Current`. Before the first
successful move and after exhaustion or a failed move, `Current` throws
`Exception("Enumerator has no current element")`. A `Current` error leaves the
wrapper's position unchanged; the input getter retains its own error behavior.

Value semantics match other query operators. A borrowed String element is copied
into the returned owning String, so editing or destroying that result does not
modify or destroy the source. Pointer elements remain pointers and do not extend
pointee lifetime. A noncopyable owning element can be returned when the active
source produces a fresh owning value; a borrowed noncopyable owner cannot be
copied by this value-returning getter. Both inputs must support the same ordinary
element return contract.
`foreach` applies these same ordinary initialization rules to each iteration value.

Both iterator fields remain alive until the Concat iterator is destroyed,
including after switching to the right input or reaching EOF. Early exit,
exceptions, and normal scope exit clean up both fields through ordinary RAII.
Keep the query's owning source state, or any externally borrowed backing owner,
alive and stable while an iterator uses it. Repeated enumeration follows each
source's normal behavior: independent views restart, while a file-line view
continues consuming its shared open file position.

For selective source builds, include `Queries/enumerable_concat.gf`,
`Queries/ConcatSource.gf`, the usual enumerable interfaces, `Exception.gf`,
and the actual input source dependencies. Concat adds no String, Unique, libc,
allocator, or collector dependency of its own.
