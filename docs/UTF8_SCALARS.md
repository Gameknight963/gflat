# Iterating UTF-8 scalar values

`std::Text::Utf8Scalars` is a borrowed view over counted UTF-8 bytes. It implements
`IEnumerable<uint, Utf8Scalars::Enumerator>` and supports ordinary `foreach`:

```cpp
using std::Text;
std::Text::Utf8Scalars text = new("hello", 5);
foreach (uint scalar in text) {
    // Each scalar is a Unicode code point, not an individual UTF-8 byte.
}
```

The pointer/count constructor accepts `readonly(char)*?` and `nuint`. Null is
accepted only when count is zero. The parameterless constructor creates an
empty view. `ByteLength` reports the captured byte count; it does not scan input.
The length getter is `nothrow`.
The view does not require String and does not own or retain an allocation.

Keep the caller's bytes alive while using the view or any of its enumerators.
Copying the view or an enumerator copies its small state and borrows the same
storage. Each `GetEnumerator()` starts independently at the first byte. Changes
to bytes not yet read are visible when they are decoded. `Current` caches the
last successful scalar, so editing those original bytes does not change it.

`AsEnumerable()` exposes the concrete lazy-query wrapper. Both it and
`GetEnumerator()` accept mutable, shallow readonly, or immutable views. The
adapter copies only borrowed pointer/count metadata and retains the same input
lifetime requirements. Queries can be enumerated repeatedly and see subsequent
changes to unread backing bytes. For example,
`text.AsEnumerable().Where(IsNonAscii).Take(10)` decodes only as far as needed
to find ten matching scalar values. Early completion does not validate a
malformed suffix. See [lazy queries](../std/core/Queries/README.md).

## Validation and errors

`MoveNext()` uses `std::Text::Utf8::TryDecode` for strict validation and returns false
at the end. U+0000 is an ordinary scalar and does not terminate enumeration.
Invalid or truncated UTF-8 throws `Exception` when reached. Construction does
not validate the entire buffer, and early termination does not inspect its
remaining bytes. On a failed decode the byte position stays unchanged, so
calling `MoveNext()` again on unchanged input throws again.

`Current` throws before the first successful `MoveNext()`, after exhaustion,
and after a decoding error. Valid enumeration allocates nothing; failures use
the normal exception runtime. `Text/utf8.gf`, the generic enumeration interface
definitions, and `Queries/Enumerable.gf` are dependencies of `Text/Utf8Scalars.gf`.
Query operators additionally require their ordinary collection source files.

Scalar values are not grapheme clusters or necessarily user-perceived
characters. No normalization, replacement, or segmentation is performed.
For input split across separately supplied chunks, use
[`Utf8Decoder`](UTF8_DECODER.md), which retains incomplete scalar state between
calls, instead of treating every chunk as a complete `Utf8Scalars` view.
