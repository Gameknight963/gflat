# Project values with their encounter index

The optional indexed `Select` overload accepts a `U(T, nuint)*` selector. It is
useful for numbered records and diagnostics without a separate index sequence.

```csharp
using std; using std::Queries; using std::Collections;
int main()
{
    int[3] data = [8, 4, 9];
    Span<int> values = new(data, 3);
    Vector<int> numbered = values.AsEnumerable()
        .Select((int value, nuint index) => value + (int)index).ToVector();
    if (numbered.Count != 3) return 1;
    if (numbered[0] != 8 || numbered[1] != 5 || numbered[2] != 11) return 2;
    return 0;
}
```

Each enumerator starts at index zero. Its index advances on successful
`MoveNext`, even when `Current` is skipped. Reading `Current` invokes the
selector every time, with the same index until the next successful move.
Projected values are not cached. `Count()` and `Any()` need not invoke a
selector or read its input value. A preceding filter changes which encounters
are indexed; a following filter does not renumber the projection.

A failing upstream `MoveNext` invalidates `Current` without advancing the
assigned index. Retrying follows the upstream iterator's ordinary behavior,
as with the one-argument `Select`. A failing input `Current` or selector leaves
the position and index unchanged; the caller can retry the getter.

Indices do not wrap. If an upstream move succeeds after index `nuint` maximum,
the adapter throws `Sequence index overflow`, with that upstream value consumed
and no current result. Later moves return false. EOF immediately after the
maximum valid index returns false normally.

Source views and cursor owners follow the same copy and lifetime rules as
ordinary `Select`. Both factories and repeated enumeration retain their source's
behavior; a consumable file is not rewound. The adapter uses no heap buffer,
but callbacks, source factories and ordinary element copies can allocate.
The callback takes `T` by value: borrowed owning values are copied, while fresh
owners can transfer normally. A selector result must not borrow storage owned
only by its temporary by-value argument. Capturing closures are not supported;
use a named function for an owning value parameter such as `String`.

Selective builds include `Queries/IndexedSelectSource.gf` and
`Queries/EnumerableIndexedSelect.gf` alongside the ordinary Enumerable,
iterator, span and exception sources. The existing adapter for a one-parameter
selector keeps its implementation and layout.
