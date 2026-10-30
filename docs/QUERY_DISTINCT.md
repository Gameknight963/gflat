# Lazy distinct values

`Distinct()` visits the first occurrence of each value in source order. It is an
optional query extension using the existing `HashSet<T>`, without materializing
the entire result before yielding its first value.

```csharp
using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;

int main()
{
    int[6] data = [3, 1, 3, 2, 1, 2];
    Span<int> values = new(data, 6);
    Vector<int> unique = values.AsEnumerable().Distinct().ToVector();
    if (unique.Count != 3 || unique[0] != 3 || unique[1] != 1 || unique[2] != 2)
        return 1;
    return 0;
}
```

`T` must support `IHashable` and `IEquatable<T>` with consistent, stable behavior:
equal values must have equal hashes. The same requirements as `HashSet<T>` apply.
Elements read from borrowed storage must be copyable because the set owns its
keys. Hashing, equality, element copies, and source iteration can allocate or
throw. No GC or compiler-specific collection support is involved.

## Laziness and ownership

The query source stores the upstream source view. Every fresh enumerator owns
its own empty seen-key set and its upstream iterator. Creating that set allocates
no backing storage; advancing can allocate as new keys are inserted. Custom source
copies or iterator construction may allocate or throw independently.

Distinct state is independent across repeated enumerations. The source itself
still determines what reenumeration means: a borrowed span starts at its first
element, while a file-line source continues from the file's current position.
Keep borrowed source storage alive and stable throughout use; composing a query
does not extend its lifetime.

The iterator stores one owned key per distinct value. Duplicate candidate values
are destroyed normally. Iterators and seen-key sets are cleaned up on normal
completion, early `Any`, `Take`, `break`, or exception unwinding. A direct iterator
variable retains its resources until it leaves scope. `Take(0)` does not advance
or allocate seen-key storage, but still constructs the upstream iterator.

Unlike a plain query's `Count` or `Any`, these terminals over `Distinct` must read
candidate values to hash and compare them. They do not bypass a preceding selector.
The source is not modified by the operation itself; callbacks and user-defined
copy/hash/equality methods can have their own effects.

## Current values are not cached

`MoveNext()` reads upstream `Current` to insert/check a key. Every read of
`Distinct`'s own `Current` reads upstream `Current` again, like the existing
`Where` adapter. It does not return the set's stored key. A preceding selector
can therefore run during `MoveNext` and again each time `Current` is read.

The value and its equality/hash meaning must remain stable at each source
position. Use stable getters and selectors when their returned value participates
in distinctness. A selector that returns different values on successive reads
does not produce reliable distinct results; this operator does not cache or
stabilize it.

The order guarantee identifies first-occurrence **positions**, not object
identity. For owning/projected values, the value returned by `Current` is a fresh
ordinary read/copy from that source position and can have a different address
from both the source object and the key retained by the set. With equality based
on only some fields, a stable source returns the first occurrence's other payload
fields too. Repeated `Current` reads follow ordinary value copying rules.

Before the first successful advance and after the end, `Current` throws. A failed
advance invalidates `Current`; it may already have advanced the source or built
some seen-key state. Dispose of that iterator by leaving its scope rather than
assuming it can recover or roll back. Exceptions from an upstream `Current`,
selector, hash/equality method, or allocation propagate normally.

## Unique lines without loading the file

```csharp
using Standard::Text;
using Standard; using Standard::IO; using Standard::Queries;

int main()
{
    File input = File::OpenRead("input.txt");
    File output = File::OpenWrite("unique.txt");
    File* source = &input;
    foreach (String line in source.Lines().AsEnumerable().Distinct())
    {
        output.Write(line.Data, line.Length);
        output.WriteLine();
    }
    input.Close();
    output.Close();
    return 0;
}
```

The line reader reuses its current String. Distinct retains independent owned
snapshots of seen lines, so replacing the current line on the next advance does
not change earlier keys. Blank lines and embedded NULs are preserved as counted
data. LF/CRLF input endings are removed by the line reader; each emitted line
gets one binary LF. Bare CR within a line remains data. Memory grows with the
unique line contents, rather than with the complete file. This is not a bounded
memory algorithm for a file containing arbitrarily many unique lines.

For explicit source selection, include `Queries/enumerable_distinct.gf` and
`Queries/DistinctSource.gf` alongside the usual query sources, `Collections/HashSet.gf`,
`Collections/HashMap.gf`, and their hash/equality dependencies. Existing queries that do not
use Distinct do not acquire a HashSet dependency merely by including its generic
definitions. The existing qualified-element `IEquatable<T>` constraint limitation
also applies here; this adapter does not erase readonly/immutable qualifiers.
