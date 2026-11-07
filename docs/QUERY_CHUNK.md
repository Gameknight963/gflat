# Enumerate owning chunks

`Enumerable.Chunk(nuint size)` divides a sequence into consecutive nonempty
`Group<T>` values. Each group contains at most size elements. The final group
may be shorter. A zero size throws when the query is created.

```csharp
using Standard::Collections;
using Standard::Queries;

int main()
{
    Vector<int> input = new();
    for (int i = 1; i <= 5; i++) input.Add(i);
    Vector<Group<int>> saved = new();
    foreach (Group<int> chunk in input.AsEnumerable().Chunk(2))
        saved.Add(chunk);
    input.Clear();
    if (saved.Count != 3 || saved[0].Count != 2 || saved[2].Count != 1) return 1;
    if (saved[0][0] != 1 || saved[1][1] != 4 || saved[2][0] != 5) return 2;
    return 0;
}
```

Groups own their element storage. Copying a group shares its immutable contents,
and indexing returns an immutable borrowed reference. Keep a group or one of
its copies alive while using those references. Saved groups remain valid when
the outer iterator advances or is destroyed. Element ownership follows T's
normal value semantics. Pointer elements still borrow their pointees.

Query creation does not enumerate input. Getting an enumerator acquires the
underlying source iterator. Each MoveNext fills only the next chunk, without
reading past a full chunk. Current returns another owning share of the same
chunk and does not read the source again. Each enumeration starts afresh.

Storage grows with the elements actually received rather than reserving size
upfront. Empty input produces no groups and allocates no chunk storage. A huge
size is therefore usable with short or empty sources. Each retained group keeps
its own contents alive.

Allocation or source failure discards the unfinished chunk, propagates the
exception, and finishes that outer iterator. Earlier published groups remain
valid. The source iterator releases its resources when the outer iterator leaves
scope. Borrowed input sources must remain alive and structurally stable during
enumeration.

Selective builds include `Queries/ChunkSource.gf`, `Queries/enumerable_chunk.gf`,
`Queries/Group.gf`, `Memory/SharedStorage.gf`, and `Threading/Interlocked.gf`,
plus the source sequence and standard value dependencies. The freestanding
form is `Chunk(source, size)`.
