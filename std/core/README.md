# Core library

Portable facilities use the replaceable `Memory` API. The default
allocator delegates to libc; replacing it does not require changing these sources.
Reference `std/std.gfproj` to include the library.

## Strings and formatting

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
`Capacity` count bytes, excluding the trailing null. Indexing operates on bytes
and checks bounds.

- `new String()` creates an empty string without allocating.
- `String::From(cString)` copies null-terminated input.
- `String::From(data, length)` copies exactly that many bytes, including nulls.
  The `s"..."` literal uses this counted form.
- `Data` and `ToString()` borrow the existing buffer. Do not free it. Mutation,
  movement, or destruction can invalidate the view; mutations can change its text.
- `Reserve`, `Append`, and `AppendLiteral` throw on overflow or allocation failure.
  Appending from the same string works, including across growth.
- `Clear` retains capacity. `Clone` makes an independent owned copy.
- `String copy = text;` makes an independent owned copy and may throw on allocation failure.
- `move text` transfers the buffer and leaves `text` uninitialized. Assign a new value before using it again.
- `Equals(readonly(String)* other)` compares bytes.

Interpolation uses `IFormattable.Format(ITextWriter*)` and
`ITextWriter.Write(readonly(char)*, nuint)`. The writer consumes bytes during the
call; it must not retain the input pointer. Strings write their existing bytes,
including embedded nulls. Primitive `std::Format(value, writer)` overloads use
stack buffers or literal text, without allocating intermediate strings.
Floating-point overloads live in `../libc/Formatting.gf`.

Only C-string consumers stop at the first null; counted operations and interpolation
preserve it. Use factories for fallible construction: constructors cannot yet
propagate exceptions.

## Vectors

`Vector<T>` owns contiguous elements. It supports primitives and owning values:

```gflat
using std;

int main()
{
    Vector<String> words = new Vector<String>();
    words.Add(s"first");
    String second = s"second";
    words.Add(move second); // second is now uninitialized.
    second = s"new value";
    words[(nuint)0].Append(c"!", 1);
    words[(nuint)1] = s"replacement";
    String last = words.Pop();
    return 0;
}
```

`Add(T)` takes ownership of its argument. Fresh values transfer directly; named
owners copy when their type supports it; use `move` to consume a local instead. `Pop()` transfers the last element out. `Clear()` destroys
all elements while retaining capacity. `Reserve(nuint)` grows storage, and the
destructor releases elements and storage. Copy a vector explicitly element by
element when that is desired; `move vector` transfers its buffer instead.

Growth relocates each element and frees the old allocation without destroying the
transferred values. No move constructor is required. Noncopyable resource types can
be elements: pass fresh values or `move` a local. `Vector<T>` itself is noncopyable
in this version; moving it transfers the buffer. Types needing a stable address must
use indirection. Growth invalidates pointers into the buffer.
Zero-sized elements are supported, but their addresses are not distinct identities.

## Spans and readonly views

`Span<T>` borrows contiguous storage without allocating or freeing it. There is
one span type; use `Span<readonly(T)>` for a view of readonly elements.

```gflat
using std;

int main()
{
    int[3] values = [10, 20, 30];
    Span<int> items = new Span<int>(values, 3);
    Span<int> tail = items.Slice(1);
    tail[(nuint)0] = 42;
    Span<readonly(int)> view = items.AsReadOnly();
    if (view[(nuint)1] != 42) return 1;
    return 0;
}
```

- Construction takes a pointer and element count. The caller provides valid,
  initialized elements. The default constructor creates an empty view.
- Lengths and indices use `nuint`. Indexing and slicing check bounds.
- A ref indexer exposes existing elements: it supports owning types, in-place
  mutation, and taking addresses. It does not copy an owner just to access it.
- Access through `readonly(Span<T>)` or `readonly(Vector<T>)` projects readonly
  elements, including deep readonly for pointer-containing elements.
- `Span<T>.AsReadOnly()` returns `Span<readonly(T)>`. Use this view when slicing
  through a readonly span; the resulting slice preserves element readonly.
- `Data` is nullable and bypasses bounds checks. Copying a span copies only its
  pointer and length.

The caller must keep storage alive. Bounds checks cannot detect dangling pointers
or incorrect counts. Readonly views observe changes through other mutable views.
A string byte view is `new Span<readonly(char)>(text.Data, text.Length)`.

## Automatically included definitions

`Attribute.gf`, `Exception.gf`, and `Text.gf` contain the compiler-recognized base
classes and formatting interfaces. They currently retain their global names and
are included automatically, once per compilation. The default memory implementation
lives in `std/libc/Memory.gf` and is also included automatically for now.

These files ship as source alongside the compiler and language server in `Std/`.
Explicit project inputs with the same std-relative paths use those source snapshots
instead of the bundled copies. Automatic inclusion and libc-free build selection
remain separate, future build configuration work.
