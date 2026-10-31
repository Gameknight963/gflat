# Borrowed span chunks

`span.Chunks(size)` yields nonoverlapping `Span<T>` groups:

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

int[7] bytes = [1, 2, 3, 4, 5, 6, 7];
Span<int> source = new(bytes, 7);
foreach (Span<int> chunk in source.Chunks(3)) {
    // [1,2,3], [4,5,6], then [7].
}
```

Size must be positive. Empty input yields nothing, while a size at least as large
as nonempty input yields the whole span once. A shorter final group is retained.

The result is SpanChunks<T>. Each enumerator starts at the beginning, and
AsEnumerable enables queries. Chunks preserve element qualifications and can
refer to noncopyable values.

Keep source storage alive for retained chunks, iterators, and queries. Changes
through writable chunks appear in the same backing storage. Container operations
that invalidate a source span also invalidate its chunks. Use
[Windows](SPAN_WINDOWS.md) for overlapping full-width groups.

Source: `core/Collections/SpanChunks.gf`
