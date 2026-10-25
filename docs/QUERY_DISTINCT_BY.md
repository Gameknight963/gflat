# Lazy distinct values by key

`query.DistinctBy(selector)` yields the first source position for each selected
key, retaining the source's order. It returns whole values rather than keys, and
the element type does not need hash or equality implementations. This is useful
for retaining the first record for each ID without collecting groups first.

```cpp
using std; using std::Queries; using std::Collections;

struct Row
{
    public int Id;
    public int Payload;
    public Row(int id, int payload) { Id = id; Payload = payload; }
}

int Id(Row value) => value.Id;

int main()
{
    Row[5] rows = [new Row(2, 20), new Row(1, 10), new Row(2, 99),
                   new Row(3, 30), new Row(1, 88)];
    Span<Row> source = new(rows, 5);
    Vector<Row> first = source.AsEnumerable().DistinctBy(&Id).ToVector();
    if (first.Count != 3) return 1;
    if (first[0].Id != 2 || first[0].Payload != 20) return 2;
    if (first[1].Id != 1 || first[1].Payload != 10) return 3;
    if (first[2].Id != 3 || first[2].Payload != 30) return 4;
    return 0;
}
```

The selector has ordinary by-value type `K(T)*`, matching `CountBy` and other
key-selector APIs. A named function can return an owning key such as String.
`K` implements `IHashable` and the matching `IEquatable<K>` through the existing
HashSet rules. Equal keys need equal hashes; their hash and equality must stay
stable while retained. The exact qualified-key interface contract applies, as
with `Distinct` and `CountBy`.

## Evaluation and state

Constructing the query copies its source view and saves the function pointer;
it does not enumerate. Captured sources follow normal value-copy rules and must
support those copies. Each `GetEnumerator()` constructs one upstream iterator
and its own empty `HashSet<K>`. The empty set allocates no backing storage.
Custom source copies or iterator construction can allocate or throw. `Take(0)`
constructs the iterator but never selects keys or allocates seen-key storage.

Each `MoveNext()` advances until it finds an unseen key or reaches EOF. It reads
upstream `Current` once and invokes the key selector once for each candidate,
including duplicates. The candidate key is moved into the set on success;
duplicate candidate owners are destroyed normally. Seen keys remain owned by
that iterator until it is destroyed. Separate enumerations have separate sets.
Stable borrowed views restart normally, while consumable sources such as file
lines continue at their shared current position.

`Current` is not cached: it reads the upstream value again without invoking
DistinctBy's key selector. Repeated reads repeat the upstream getter, including
an upstream `Select` callback. `Count()` and `Any()` still select and inspect
candidate keys because distinctness is determined during advancement; they need
not read the accepted value again. The same stable-provider requirement as
`Distinct` applies: values must keep their intended key and payload meaning at a
source position. The guarantee concerns first-occurrence positions, not owner
addresses or a stored snapshot of the selected element.

## Ownership and failures

By-value selector arguments follow ordinary value semantics. Borrowed owning
elements such as String are copied into the selector parameter. An upstream
fresh owning value can transfer into it. A borrowed noncopyable element cannot
be passed by value; no implicit ownership transfer from source storage occurs.
Returned keys must own their required resources independently, or point to
external storage that remains alive and stable throughout iteration. In
particular, a key must not borrow storage owned only by the selector's temporary
by-value parameter. Moving a fresh noncopyable key into HashSet is supported.

Reading `Current` returns an ordinary value, so borrowed owning elements are
copied again when yielded. Fresh by-value results transfer under the normal
initializer and `foreach` rules. The iterator never duplicates or steals the
header of a borrowed owner. Borrowed source backing storage must remain alive
and stable for the query and all iterators. Callbacks must not structurally
mutate the source or the seen-key set through another alias.

Before the first successful advance and after EOF, `Current` throws
`Exception("Enumerator has no current element")`. A failed advance invalidates
`Current` and may already have advanced the source or retained earlier keys.
Discard the iterator after such an error; no recovery or rollback is promised.
An error from the upstream getter, selector, hash/equality method, copy, or
allocation propagates unchanged. A `Current` read does not itself change the
wrapper's position. Ordinary RAII destroys retained keys and the upstream
iterator on scope exit, including early termination and exception unwinding.

Memory grows with the distinct selected keys. The operator does not allocate a
wrapper per element, use GC, or materialize all source values. Key/source/element
operations retain their own allocation behavior, and HashSet retains its normal
collision and capacity limits.

For selective sources include `Queries/enumerable_distinct_by.gf`,
`Queries/DistinctBySource.gf`, the ordinary query interfaces, HashSet,
HashMap, and their hashing/equality dependencies, plus the actual input types.
