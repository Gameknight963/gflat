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
- `String::From(data, length)` and `s"..."` copy input into owned storage.
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
