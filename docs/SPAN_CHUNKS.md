# Borrowed span chunks

`span.Chunks(size)` partitions a span into nonoverlapping `Span<T>` views. Every
source element appears once, in order. The final chunk may contain fewer than
`size` elements; no padding or element copying occurs.

```csharp
using std; using std::Collections;
int[7] bytes = [1, 2, 3, 4, 5, 6, 7];
Span<int> source = new(bytes, 7);
foreach (Span<int> chunk in source.Chunks(3)) {
    // [1,2,3], [4,5,6], then [7].
}
```

Size must be positive, including for empty input. Empty input produces no chunks;
a size greater than or equal to a nonempty source length produces the whole span
once. Cursor advancement is bounded by the remaining source length, including
when size is the largest `nuint`.

The result is `SpanChunks<T>`, with a nested `Enumerator`, `GetEnumerator()`, and
`AsEnumerable()` for normal lazy query operators. Each enumeration has its own
position and starts again from the beginning. `Current` throws before the first
successful move and after exhaustion; repeated moves after exhaustion return
false. View construction and enumeration allocate nothing and do not copy or
destroy elements. Noncopyable owning elements are supported.

All results borrow the same source allocation. Mutations through a chunk or
another valid alias remain visible, including in a later enumeration. The view
captures pointer and length metadata, so changing or destroying the original
span header does not change existing chunks or extend the storage lifetime.
The caller keeps the underlying storage alive and at the same address for every
view, iterator, and retained chunk. Container operations that invalidate source
borrows invalidate these views too. Raw views do not root managed storage.

`T` retains its qualifications: `AsReadOnly().Chunks(...)` protects element
storage, while `AsImmutable().Chunks(...)` also protects storage reached through
elements. Enumeration and `AsEnumerable()` require a mutable view header, matching
`Span` and [sliding windows](SPAN_WINDOWS.md). Windows yield only full windows
and may overlap; chunks are disjoint and retain a shorter final piece.

Public view and iterator constructors accept null storage only with zero length.
The caller supplies a valid non-null extent. Consumers and query callbacks retain
their ordinary allocation, copying, and exception behavior.

When selecting sources explicitly, include `Collections/SpanChunks.gf`,
`Span.gf`, `Collections/IEnumerable.gf`, `Collections/IEnumerator.gf`, and
`Collections/Enumerable.gf`, plus the query operator files actually used. Projects
including the full standard library already include these sources.
