# Adding a query endpoint

`Append(value)` yields the source values followed by one captured value.
`Prepend(value)` yields the captured value before the source. They create lazy,
repeatable queries without changing the source collection. An empty source still
yields the one endpoint. An infinite source never reaches an appended endpoint.

Use [DefaultIfEmpty(value)](QUERY_DEFAULT_IF_EMPTY.md) when the captured value
should be yielded only if the source is empty.

```csharp
using Standard; using Standard::Memory; using Standard::Queries;
int main()
{
    int[2] data = [1, 2];
    Span<int> values = new(data, 2);
    var query = values.AsEnumerable().Prepend(0).Append(3);
    int expected = 0;
    foreach (int value in query)
    {
        if (value != expected) return 1;
        expected++;
    }
    return expected - 4;
}
```

The endpoint follows normal value semantics when the query is built. Passing
a named owning value copies it; `Append(move value)` or `Prepend(move value)`
transfers it into the captured query state. Each enumerator owns its own copy
of the captured endpoint. Reusable endpoint queries therefore require a stored
element type that supports that copy; moving a noncopyable owner into the
factory does not make repeated enumeration possible. No implicit borrowing
pointer or lifetime extension is introduced. Pointer endpoints copy addresses
and retain their normal pointee ownership rules.

`GetEnumerator()` constructs the underlying iterator before returning the new
iterator, as existing `Where` and skip/take operators do. Construction can copy
the endpoint or source state and can allocate or throw according to those types.
It does not call underlying `MoveNext` or `Current`. Even
`Prepend(value).Take(1)` constructs the underlying iterator, though it never
advances that iterator. The first successful move of `Prepend` selects only the
endpoint. `Append` reads the source until its first false move, then selects the
endpoint without reading source `Current` again. Finished queries never advance
the source again.

`Current` returns an ordinary `T` value without moving out of captured state.
Repeated endpoint reads copy the same stored value; repeated source reads
delegate to the underlying `Current` and retain its property effects, such as
repeated `Select` evaluation. `Current` throws before the first successful move,
after completion, or after a failed move. A failed `Current` copy does not advance
the iterator. An underlying `MoveNext()` exception propagates and permanently
finishes this wrapper iterator: later moves return false without retrying the
source, and `Current` is invalid. A new enumeration starts a fresh iterator.

The wrapper adds constant element/state storage and no heap allocation of its
own. Owning endpoint copies, custom source copies, and underlying iterators can
allocate. Normal cleanup releases the endpoint and underlying iterator on early
exit, downstream exceptions, or iterator destruction. The query's separately
captured endpoint remains available for another enumeration. Usual source view
lifetime and mutation rules still apply.

The endpoint type is the sequence's existing `T`; these operators do not widen
types or remove element qualification. The factories transfer their value
parameter using `move`, which cannot consume outer `readonly` or `immutable`
storage. Project to an
ordinary value before capturing endpoints, for example
`.Select((readonly(int) value) => (int)value).Append(3)` for a readonly integer
view. This is the same qualified-storage limitation that affects buffering and
some other query terminals.

Selective source users add
`Queries/EndpointSource.gf` and `Queries/enumerable_endpoints.gf` with the
normal sequence contracts. The wrapper makes no direct libc or collector calls
and needs no Vector or String implementation. Dependencies of its element,
iterator, and exception runtime still apply.
