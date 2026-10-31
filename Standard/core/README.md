# Core library

Core provides portable strings, collections, queries, text processing, and binary
codecs. Owning values allocate through the replaceable `Memory` API.

```csharp
using Standard::Text;
using Standard;

int main()
{
    String name = s"Ada";
    String message = s$"Hello {name}, score: {42}";
    message.Append("!", 1);
    return 0;
}
```

## Strings and formatting

`String` owns a mutable byte buffer and frees it at scope exit. `s"..."` creates a
String, and `s$"..."` formats values into one. `ITextWriter` accepts counted bytes
for output into strings, buffers, files, or other writers.

See [strings](../../docs/STRINGS.md), [string conversion](../../docs/STRING_CONVERSION.md),
and [formatting](../../docs/CORE_LIBRARY.md#strings-and-formatting).

## Vectors

`Vector<T>` owns contiguous elements. `Add` appends a value, `Pop` transfers the last
value to the caller, and `Clear` destroys the elements while retaining capacity.
Use `move` to transfer a named owner into a collection.

See [vector ownership](../../docs/CORE_LIBRARY.md#vectors) and
[collection operations](Collections/OWNING_COLLECTIONS.md).

## Spans and readonly views

`Span<T>` borrows contiguous storage. `Span<readonly(T)>` protects elements, and
`Span<immutable(T)>` also protects storage reachable through them. Keep the owner
alive and reacquire spans after operations that relocate its storage.

See [spans](../../docs/CORE_LIBRARY.md#spans-and-readonly-views) and
[span algorithms](Collections/ALGORITHMS.md).

## Integer parsing

`TryParseInt` reports success through a Boolean and writes the result through a
pointer. `ParseInt` returns the value or throws for invalid input.
See [integer parsing](../../docs/INTEGER_PARSING.md).

## Integer sequences

`IntRange` describes consecutive integers. Import `Standard::Queries` for lazy
filtering and slicing, or use `AsEnumerable()` for the general query operators.
See [ranges](../../docs/CORE_LIBRARY.md#integer-sequences) and
[queries](Queries/README.md).

## Owning an exception message

`Exception` borrows its message. A derived exception can own a String and return
its bytes from `GetMessage()`. See the
[complete example](../../docs/CORE_LIBRARY.md#owning-an-exception-message).

## Automatically included definitions

Standalone compilation includes base types and runtime contracts. Project builds
use their evaluated source graph, including Standard by default. `NoStdLib`
disables automatic Standard sources.

See [source selection](../../docs/PROJECTS.md#standard-library-selection).

## Static type metadata

`typeof(T)` describes a static type's name, layout, and recursive type structure.
See [type metadata](../../docs/CORE_LIBRARY.md#static-type-metadata).
