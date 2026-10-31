# Iterating UTF-8 scalar values

`Standard::Text::Utf8Scalars` provides `foreach` over counted UTF-8:

```cpp
using Standard::Text;

Utf8Scalars text = new("hello", 5);
foreach (uint scalar in text) {
    // Each scalar is a Unicode code point, not an individual UTF-8 byte.
}
```

The constructor takes a nullable character pointer and byte count. Null is valid
for empty input. `ByteLength` reports the captured byte count.

Each `GetEnumerator()` starts at the beginning. `Current` caches the last decoded
`uint`. Copies have independent positions over the same bytes. Keep those bytes
alive while using a view, iterator, or query. Edits to unread bytes affect later
decoding.

`AsEnumerable()` supports [queries](../Standard/core/Queries/README.md), such as
`text.AsEnumerable().Where(IsNonAscii).Take(10)`.

## Validation and errors

Validation happens as `MoveNext()` decodes each scalar. Malformed UTF-8 throws
`Exception` and invalidates `Current`. The position stays at the malformed
sequence, so retrying unchanged input throws again.

Breaking early leaves the remaining input unvalidated. For full validation,
use `Utf8::IsValid`. For chunks that may split a scalar, use
[Utf8Decoder](UTF8_DECODER.md).

Source: `core/Text/Utf8Scalars.gf`
