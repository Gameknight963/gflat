# Order a buffered query

`Order()` returns a query sorted by `Comparer<T>::Compare`, requiring
`T : IComparable<T>`. `Order(compare)` accepts an ordinary function pointer of
type `int(immutable(T)*, immutable(T)*)*` without that interface requirement. A
negative result places the first argument earlier; zero preserves original
encounter order. For descending order, supply a comparator with reversed order.

```csharp
using std; using std::Queries;

int Descending(immutable(int)* first, immutable(int)* second)
{
    if (*first > *second) return -1;
    if (*first < *second) return 1;
    return 0;
}

int main()
{
    int[4] data = [3, 1, 2, 1];
    Span<int> values = new(&data[0], 4);
    var ascending = values.AsEnumerable().Order();
    var descending = values.AsEnumerable().Order(&Descending);
    if (ascending.First() != 1 || descending.First() != 3) return 1;
    if (data[0] != 3 || data[1] != 1) return 2;
    return 0;
}
```

Creating a query or its cursor does not enumerate the underlying source or
allocate its element buffer. The first `MoveNext` materializes the entire input
with `ToVector`, then calls the existing `StableSort` once. Only after success
does the cursor retain that buffer and yield its first item. Even `Take(1)` needs
the whole input; `Take(0)` never starts materialization. A source that does not
end cannot produce the first ordered element.

Source/query values still follow ordinary copy rules: custom source copies can
allocate or throw while wrappers are constructed. Built-in borrowed views just
copy their metadata. Changes before materialization are visible; later changes
do not alter that cursor's buffered value copies. A new enumeration takes a new
buffer and sorts independently. Re-enumeration follows the source: a consumable
source such as FileLineSequence continues from its current position rather than
replaying previously read input. Borrowed/pointer elements keep their ordinary
shared-target lifetime rules, and the backing source must stay valid as required
by its adapter.

`Current` returns `ref immutable(T)` into the owned buffer. Inspecting it does not
copy an owning element. The reference borrows that cursor's buffer and must not
outlive the cursor. MoveNext controls whether Current can be obtained; ordinary
borrow and alias rules still apply. A by-value consumer invokes normal copy
semantics, so noncopyable values require borrowed access through their concrete
cursor. Materializing those values also requires a source that produces fresh or
moved owning values: ToVector cannot copy a borrowed noncopyable element.
The cursor follows the existing concrete foreach pattern and does not implement
the value-returning `IEnumerator<T>` contract. Current is invalid before a
successful move, after exhaustion, or after failed initialization. A consumer copy
failure leaves the buffered position valid and unchanged.

If source construction, enumeration, copying, allocation, or comparison throws,
partial materialization/sort storage is destroyed and the original error
propagates. That cursor stays finished: later moves return false without retrying,
and Current remains invalid. A new enumeration can try again. Buffered storage is
released at cursor destruction, including after early exit; successful cursors
retain it after exhaustion until then. Comparators must obey StableSort's existing
ordering, lifetime, and source-mutation requirements.

The element type must fit the existing `Vector<T>` storage contract. Outer
readonly/immutable element types are not silently changed: explicitly project a
readonly scalar view to its ordinary value type before calling Order, as with
Reverse/ToVector. Sorting uses O(n) element storage plus StableSort's two index
vectors and O(n log n) comparisons before yielding the first result. Later moves
are constant-time, with ordinary consumer copy costs when applicable.

Selective sources include `Queries/OrderSource.gf`,
`Queries/enumerable_order.gf`, `Queries/enumerable_to_vector.gf`,
`Collections/stable_sort.gf`, `Collections/Comparer.gf` for default ordering, and the normal
Enumerable/Vector dependencies.
