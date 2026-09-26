# Core library

Portable facilities live here. They use the replaceable `Allocator` API, not libc
calls directly. The default allocator in the prelude currently delegates to libc;
a replacement allocator can supply storage without changing these sources.

## Strings

```gflat
using std;

int main()
{
    String name = s"Ada";
    String message = s$"Hello {name}, score: {42}";
    message.Append(c"!", 1);
    return 0;
}
```

`String` owns a mutable UTF-8 buffer and frees it at scope exit. `Length` and
`Capacity` are `nuint` byte counts excluding the trailing null terminator. Indexing
returns a byte-sized `char`, not a Unicode code point, and checks bounds.

- `new String()` creates an empty string without allocation.
- `String::From(cString)` copies null-terminated input up to its first null.
- `String::From(data, length)` copies exactly that many bytes, including embedded
  nulls; the input need not have a terminator. `s"..."` uses this counted form.
- `Data` exposes a readonly pointer. It remains valid until growth or destruction;
  mutations can change its contents.
- `Reserve`, `Append`, and `AppendLiteral` report overflow/allocation failure by
  throwing. Appending from the same string is supported, including across growth.
- `Clear` retains capacity. `Clone` makes an independent copy.
- `Equals(readonly(String)* other)` compares bytes.
- `ToString()` returns a separate owned C-string; callers must use
  `Allocator::Free`. Interpolation does this automatically.

Strings can contain embedded null bytes. Passing `Data` to a C-string API, or
converting a string through interpolation's `ToString` contract, stops at the
first null. Byte-counted operations preserve all bytes.

Like other destructor-bearing values, `String` is not implicitly copyable. Borrow
it through `readonly(String)*` or `String*`, or explicitly `Clone` it. Use factories
for fallible construction; the language does not yet support throwing constructors.

`Formatting.gf` supplies allocated decimal conversions for integer types, and
text conversions for booleans and characters. Floating-point conversions live in
`../libc/Formatting.gf`.

## Spans

`Span<T>` and `ReadOnlySpan<T>` borrow contiguous storage without allocating or
freeing it. Include both `Span.gf` and `ReadOnlySpan.gf`, or reference `std.gfproj`.
Lengths and indices use `nuint`.

```gflat
using std;

int main()
{
    int[3] values = [10, 20, 30];
    Span<int> items = new Span<int>(values, 3);
    Span<int> tail = items.Slice(1);
    tail[(nuint)0] = 42; // Also changes values[1].
    ReadOnlySpan<int> view = items.AsReadOnly();
    if (view[(nuint)1] != 42) return 1;
    return 0;
}
```

- Construct from a non-null pointer and element count; the caller must provide
  that many valid, initialized elements. The count is not inferred or verified.
- The parameterless constructor creates an empty view with null `Data`.
- `Length` and `IsEmpty` describe the view. Indexing checks `index < Length`.
- `Slice(start)` and `Slice(start, count)` borrow part of the same buffer. Invalid
  ranges throw; empty slices are valid, including a slice at `Length`. Empty
  slices are normalized to null `Data`.
- Copying a span copies its pointer and length, not the elements.
- Indexers return elements by value. Use copyable element types; owning values
  such as `String` cannot be implicitly copied through an indexer. Use pointers
  to such objects when appropriate. An indexer is not a reference return.
- `Span<T>.AsReadOnly()` borrows the same elements through a readonly pointer.
  `ReadOnlySpan<T>` has no setter and returns elements through `readonly(T)`,
  preserving deep readonly for pointer-containing types. A `readonly(Span<T>)`
  permits metadata access and `AsReadOnly`; use that readonly view for indexing
  and slicing. The mutable span indexer and `Data` require a mutable receiver.
- `Data` is nullable and bypasses bounds checking. `Span<T>` exposes a mutable
  pointer; `ReadOnlySpan<T>` exposes a pointer to readonly elements.

The caller must keep the underlying storage alive. Do not return a span into a
local array or use a span after its owner is destroyed or reallocates its buffer.
Readonly views observe changes made through other mutable views; they are not
snapshots. Bounds checks cannot detect a dangling pointer or an incorrect count.

For a string view, use
`new ReadOnlySpan<char>(text.Data, text.Length)`. Counts include embedded nulls,
exclude the trailing terminator, and measure UTF-8 bytes, not Unicode characters.
