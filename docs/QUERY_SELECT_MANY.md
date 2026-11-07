# Flattening projected sequences

`SelectMany(selector)` projects each outer element into an `Enumerable` and
yields that sequence's elements before advancing the outer sequence. Empty
inner sequences contribute no elements. Generic arguments are inferred from
the selector's return type.

```csharp
using Standard::Queries;

Enumerable<int, IntRange, IntRange::Enumerator> Expand(int count)
    => new IntRange(10, count).AsEnumerable();

int main()
{
    var query = new IntRange(0, 4).AsEnumerable().SelectMany(Expand);
    int[6] expected = [10, 10, 11, 10, 11, 12];
    nuint index = 0;
    foreach (int value in query)
    {
        if (index >= 6 || value != expected[index]) return 1;
        index++;
    }
    if (index != 6) return 2;
    return 0;
}
```

Load `Queries/SelectManySource.gf`, `Queries/enumerable_select_many.gf`, and
`Memory/Unique.gf` alongside the query and sequence dependencies. The free
function `SelectMany(source, selector)` provides the same operation.

## Evaluation and lifetime

Constructing the query does not call the selector. Each cursor obtains a fresh
outer iterator. Advancing it calls the selector once for each visited outer
element, and starts only one inner iterator at a time. Reading `Current` forwards
to the active inner iterator without calling the selector again.

The cursor owns the projected query and its iterator. Their storage stays at a
stable address across moves of the flattening cursor. The inner iterator is
destroyed before its query, and both are released before advancing the outer
iterator. Early return or `break` releases the active inner state when the
flattening cursor leaves scope.

Borrowed collection storage must remain alive and stable for its enumeration.
Projected source types follow `Enumerable`'s existing copy requirements. Inner
iterators can own resources and be noncopyable.
The selector receives its outer argument by value. A returned query must not
borrow that parameter's temporary storage. Use pointer elements when an inner
query needs to borrow an existing outer object's storage. Pointer results retain
their ordinary borrowed lifetime.

## Storage and failures

One heap allocation holds each projected query and iterator, including an empty
inner sequence. At most one such allocation remains active. Source copying,
element copying, and custom callbacks may allocate separately. Flattening does
not buffer the combined output.

Selector, iterator, element-copy, and allocation failures propagate. Failure
while advancing or reading an inner value invalidates `Current`, releases the
active inner state, and makes later `MoveNext` calls return false. The outer
iterator is released when the flattening cursor leaves scope. Previously
consumed input is not rolled back.

`Current` throws before a successful move and after the end. Re-enumerating a
reusable outer source starts again and runs the selectors again. External
stateful sources retain their own replay rules.
