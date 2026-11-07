# Zip sequences without a selector

`left.Zip(right)` yields `Zipped<T, U>` values with public `First` and `Second`
fields. It ends when either sequence ends. The existing selector overload
remains available when a different result type is useful.

```csharp
using Standard::Collections;
using Standard::Queries;

int main()
{
    Vector<int> first = new();
    first.Add(1);
    first.Add(2);
    Vector<int> second = new();
    second.Add(10);
    second.Add(20);
    second.Add(30);
    int sum = 0;
    foreach (Zipped<int, int> pair in first.AsEnumerable().Zip(second.AsEnumerable()))
        sum += pair.First + pair.Second;
    return sum - 33;
}
```

Zipped is an ordinary value type. Its constructor takes both values and moves
them into its fields. Copies follow the fields' usual value semantics, so
owning fields are independent when copyable. Move-only values can be zipped
when the source getters produce fresh values. Pointer fields retain their
ordinary borrowing semantics.

The query is deferred and allocates no adapter storage. Source-view copies,
iterator construction, and element getters or copies can allocate or throw.
Each Current access reads both source values again and constructs a fresh pair.
A pair can outlive its iterators when its fields own their data. Keep borrowed
sources alive and structurally stable while enumerating.

The left iterator advances first. When the right sequence ends first, the
left iterator can have advanced one extra element. Source iterators clean up
when the zip iterator leaves scope, including on failure.

Selective builds include `Queries/Zipped.gf`, `Queries/ZipSource.gf`, and
`Queries/enumerable_zip_pairs.gf`, plus source sequence dependencies. The
freestanding form is `Zip(left, right)`.
