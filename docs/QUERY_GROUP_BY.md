# Grouping a query by key

`GroupBy(keySelector)` yields owning `Grouping<K, T>` values. Groups follow the
order in which their keys first appear, and values within each group retain
source order. `GroupBy(keySelector, elementSelector)` stores projected values.

```csharp
using Standard::Memory;
using Standard::Queries;

int Key(int value) => value % 3;

int main()
{
    int[6] values = [2, 1, 5, 0, 4, 3];
    var query = new Span<int>(&values[0], 6).AsEnumerable().GroupBy(Key);
    int[3] keys = [2, 1, 0];
    nuint index = 0;
    foreach (Grouping<int, int> group in query)
    {
        if (index >= 3 || group.Key != keys[index] || group.Count != 2) return 1;
        index++;
    }
    if (index != 3) return 2;
    return 0;
}
```

The free functions `GroupBy(source, keySelector)` and
`GroupBy(source, keySelector, elementSelector)` provide the same operations.
Generic arguments are inferred. Load `Queries/Grouping.gf`,
`Queries/GroupBySource.gf`, and `Queries/enumerable_group_by.gf` with
`Memory/SharedStorage.gf`, `Threading/Interlocked.gf`, `HashMap`, `Vector`, and
their ordinary dependencies.

## Owning keys and values

Each grouping owns its first representative key and its values. Equal later
keys contribute to the same group and are released after lookup. Keys must
implement `IHashable` and `IEquatable<K>`, with equal keys producing equal hashes.
Fresh noncopyable keys and move-only projected elements are supported.

`Key` borrows an immutable reference, so reading it does not copy an owning key.
`Count` and `IsEmpty` describe the values. `GetEnumerator()` exposes immutable
element references, and `AsEnumerable()` composes further value queries.

Copying a grouping shares its storage without cloning keys or values or
allocating buffer storage. Groupings and their value iterators retain both the
key and values independently of the source and grouping cursor. Reference counts
are atomic, with the same handle-sharing rules as [Group](QUERY_GROUP_JOIN.md).
Retain an owner while using borrowed key or element references. Pointer keys
and elements retain their ordinary borrowed-pointee lifetime.

```csharp
using Standard::Memory;
using Standard::Queries;

int Key(int value) => value % 2;
int Project(int value) => value * 10;

Grouping<int, int> SavedGroup()
{
    int[3] values = [1, 2, 3];
    return new Span<int>(&values[0], 3).AsEnumerable().GroupBy(Key, Project).First();
}

int main()
{
    Grouping<int, int> saved = SavedGroup();
    if (saved.Key != 1 || saved.Count != 2) return 1;
    var iterator = saved.GetEnumerator();
    if (!iterator.MoveNext() || iterator.Current != 10) return 2;
    if (!iterator.MoveNext() || iterator.Current != 30 || iterator.MoveNext()) return 3;
    return 0;
}
```

## Evaluation and failures

Construction and `GetEnumerator()` capture source values without enumerating.
The first `MoveNext` buffers the entire source before yielding any grouping.
An infinite source cannot produce a result. Each source `Current` and key
selector is read once per element. The element selector runs after key lookup,
once for each successfully processed element. Re-enumeration rebuilds the groups.

By-value key callbacks require copyable source elements. The captured current
value is moved into the element selector after computing its key. Use pointer
elements for borrowed noncopyable source objects. Callbacks must not return
borrowed storage from their temporary by-value parameters.

Iterator, selector, hash/equality, copy, and allocation errors propagate and
release partially built groups. No partial grouping is yielded. A failed cursor
ends, and later `MoveNext` calls return false. `Current` throws before a
successful move and after the end. Completed groupings already retained by the
caller remain valid when the cursor finishes. Reference-count overflow throws
before adding a share.
