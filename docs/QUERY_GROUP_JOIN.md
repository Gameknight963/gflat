# Grouping matches for each outer value

`outer.GroupJoin(inner, outerKey, innerKey, resultSelector)` supplies a
`Group<U>` containing all matching inner values for each outer value. An outer
value with no matches still produces a result, with an empty group.

```csharp
using Standard::Queries;

int Key(int value) => value % 3;
int CountMatches(int outer, Group<int> matches) => outer * 10 + (int)matches.Count;

int main()
{
    var outer = new IntRange(0, 4).AsEnumerable();
    var inner = new IntRange(0, 2).AsEnumerable();
    var query = outer.GroupJoin(inner, Key, Key, CountMatches);
    int[4] expected = [1, 11, 20, 31];
    nuint index = 0;
    foreach (int value in query)
    {
        if (index >= 4 || value != expected[index]) return 1;
        index++;
    }
    if (index != 4) return 2;
    return 0;
}
```

The free function `GroupJoin(outer, inner, outerKey, innerKey, resultSelector)`
accepts the same arguments. Generic arguments are inferred. Load
`Queries/Group.gf`, `Queries/GroupJoinSource.gf`, and
`Queries/enumerable_group_join.gf` with `Memory/SharedStorage.gf`,
`Threading/Interlocked.gf`, `HashMap`,
`Vector`, and their ordinary dependencies.

## Retaining groups

`Group<T>` owns shared immutable value storage. Copying a group or creating its
iterator adds a reference without cloning elements or allocating buffer storage.
Groups can outlive the join cursor and original input containers. Pointer
elements retain their borrowed lifetime and do not keep pointees alive.

```csharp
using Standard::Queries;

int Key(int value) => value % 2;
Group<int> Keep(int outer, Group<int> matches) => move matches;

Group<int> SavedMatches()
{
    var outer = new IntRange(1, 1).AsEnumerable();
    var inner = new IntRange(0, 4).AsEnumerable();
    return outer.GroupJoin(inner, Key, Key, Keep).First();
}

int main()
{
    Group<int> saved = SavedMatches();
    var iterator = saved.GetEnumerator();
    saved = new Group<int>();
    if (!iterator.MoveNext() || iterator.Current != 1) return 1;
    if (!iterator.MoveNext() || iterator.Current != 3) return 2;
    if (iterator.MoveNext()) return 3;
    return 0;
}
```

Groups provide `Count`, `IsEmpty`, `GetEnumerator()`, and `AsEnumerable()`.
Enumeration exposes immutable element references. An iterator or captured query
retains its own share until it leaves scope or is replaced, including after
enumeration finishes. The final owner releases stored values and buffers.
Retain an owner while using borrowed element references. Empty groups need no
storage allocation.

Reference counts are atomic, allowing separate handles to be copied and
destroyed concurrently. Replacing the same group variable requires caller
synchronization. Concurrent use of stored objects follows their own rules.

## Evaluation and errors

Results follow outer order, and each group's values follow original inner
order. Duplicate outer keys share the same buffered values. Each cursor builds
its own lookup, so re-enumeration observes the sources again.

Construction does not enumerate either source. The first move advances the
outer iterator before buffering the entire inner sequence. An empty outer skips
inner enumeration. An infinite inner cannot produce a result. Each visited
outer `Current` and key selector is read once. Unlike an inner join, an empty
inner still visits every outer value and supplies empty groups.

Reading result `Current` invokes the result selector. Reading it again invokes
that selector again. Result values may be move-only owners. Retained groups
remain valid if a later callback or iterator operation fails.

Keys follow `HashMap` semantics and must implement `IHashable` and
`IEquatable<K>`. Fresh noncopyable keys are supported. By-value callbacks require
copyable input elements. Keep borrowed source storage and pointees alive while
enumerating, and avoid returning pointers into callback parameter temporaries.

Source, callback, hash/equality, copy, and allocation errors propagate and end
the cursor. Its lookup and current group are released, while escaped group
owners retain their values. Later `MoveNext` calls return false, and `Current`
throws. Consumed input is not rolled back. Reference-count overflow throws
before adding a new share.
