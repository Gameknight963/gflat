# Sliding windows over borrowed spans

Include `core/Collections/SpanWindows.gf` alongside `Span.gf`, `IEnumerable.gf`,
`IEnumerator.gf`, and `Enumerable.gf` when selecting standard-library sources.
The full standard library includes these sources automatically.

```csharp
using std;
int[5] samples = [1, 3, 5, 7, 9];
Span<int> values = new(samples, 5);
foreach (Span<int> pair in values.Windows(2)) {
    int difference = pair[1] - pair[0];
}
```

`Windows<T>(this Span<T> source, nuint width, nuint step = 1)` returns
`SpanWindows<T>`. Every result is a full `Span<T>` of `width` elements; starts are
0, `step`, twice `step`, and so on while the full window fits. No partial final
window is produced. Width and step must be positive, even for an empty source.
For nonoverlapping groups that retain a shorter final piece, use
[chunks](SPAN_CHUNKS.md).
A width greater than the source length produces no windows. Cursor advancement
checks the remaining distance before addition, including very large steps.

The view and its nested `Enumerator` copy only a pointer and numeric metadata.
They do not copy, own, allocate, or destroy source elements. Noncopyable elements
are supported. Overlapping mutable windows refer to the same elements, so writes
through an earlier window can change values observed by later windows. `T`'s
qualifiers are preserved: `AsReadOnly().Windows(...)` protects element storage;
`AsImmutable().Windows(...)` also protects storage reached through its elements.
Enumeration and `AsEnumerable()` require a mutable view header, as with `Span`.

`GetEnumerator()` creates an independent cursor. `Current` throws before the
first successful `MoveNext()` and after iteration ends. Repeated `MoveNext()`
after the end returns false. The public constructors reject null storage with a
nonzero length; the caller supplies a valid extent for non-null storage.

The captured pointer and length do not depend on the original span header.
Changing or destroying that header does not change an existing windows view.
Source element edits remain visible. The caller keeps the underlying storage
alive and at the same address for every enumeration and every retained window;
container operations that invalidate source borrows invalidate these views too.
A view does not extend an owning container's lifetime or root managed storage.

`AsEnumerable()` supports the existing lazy query extensions, without copying
elements or allocating for view construction/enumeration. For example:

```csharp
int Average(Span<readonly(int)> window)
    => (window[0] + window[1] + window[2]) / 3;

var averages = values.AsImmutable().Windows(3).AsEnumerable().Select(Average);
```

Include the normal query extension/source files when using `Select` and other
operators. Consumer callbacks and materializing terminal operations retain their
own allocation, copying, and exception behavior.
