# Iterating UTF-8 scalar values

`std::Utf8Scalars` is a borrowed view over counted UTF-8 bytes. It implements
`IEnumerable<uint, Utf8Scalars::Enumerator>` and supports ordinary `foreach`:

```cpp
std::Utf8Scalars text = new("hello", 5);
foreach (uint scalar in text) {
    // Each scalar is a Unicode code point, not an individual UTF-8 byte.
}
```

The pointer/count constructor accepts `readonly(char)*?` and `nuint`. Null is
accepted only when count is zero. The parameterless constructor creates an
empty view. `ByteLength` reports the captured byte count; it does not scan input.
The view does not require String and does not own or retain an allocation.

Keep the caller's bytes alive while using the view or any of its enumerators.
Copying the view or an enumerator copies its small state and borrows the same
storage. Each `GetEnumerator()` starts independently at the first byte. Changes
to bytes not yet read are visible when they are decoded. `Current` caches the
last successful scalar, so editing those original bytes does not change it.

## Validation and errors

`MoveNext()` uses `std::Utf8::TryDecode` for strict validation and returns false
at the end. U+0000 is an ordinary scalar and does not terminate enumeration.
Invalid or truncated UTF-8 throws `Exception` when reached. Construction does
not validate the entire buffer, and early termination does not inspect its
remaining bytes. On a failed decode the byte position stays unchanged, so
calling `MoveNext()` again on unchanged input throws again.

`Current` throws before the first successful `MoveNext()`, after exhaustion,
and after a decoding error. Valid enumeration allocates nothing; failures use
the normal exception runtime. `Utf8.gf` and the generic enumeration interface
definitions are dependencies of `Utf8Scalars.gf`.

Scalar values are not grapheme clusters or necessarily user-perceived
characters. No normalization, replacement, or segmentation is performed.
For input split across separately supplied chunks, use
[`Utf8Decoder`](UTF8_DECODER.md), which retains incomplete scalar state between
calls, instead of treating every chunk as a complete `Utf8Scalars` view.
