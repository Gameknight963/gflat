# Padded Base32

`std::Base32` implements standard padded Base32 with uppercase `A–Z` and `2–7`.
It follows [RFC 4648 sections 3 and 6](https://www.rfc-editor.org/rfc/rfc4648.html#section-6).
It adds no whitespace, line breaks, or terminator. Lowercase, aliases such as
`0`/`1`, nonalphabet bytes, missing/excess/misplaced padding, and nonzero unused
padding bits are rejected. This is neither base32hex nor Crockford Base32.

```csharp
using std;
int main()
{
    char[8] encoded;
    char[3] decoded;
    nuint written = 0;
    if (!Base32::TryEncode("foo", 3, encoded, 8, ref written) || written != 8) return 1;
    for (nuint i = 0; i < 8; i++) if (encoded[i] != "MZXW6==="[i]) return 2;
    if (!Base32::TryDecode(encoded, 8, decoded, 3, ref written) || written != 3) return 3;
    for (nuint i = 0; i < 3; i++) if (decoded[i] != "foo"[i]) return 4;
    return 0;
}
```

All four methods are `nothrow` and allocate nothing:

- `TryGetEncodedLength(count, ref length)` returns the padded byte count, or false
  with zero length if it would overflow `nuint`.
- `TryEncode(data, count, output, capacity, ref written)` consumes counted binary
  bytes, including embedded NUL. It checks encoded size and capacity before writes.
- `TryGetDecodedLength(text, count, ref length)` validates the entire counted
  encoding, returning its decoded size or false with zero length.
- `TryDecode(text, count, output, capacity, ref written)` validates the complete
  input and checks capacity before writing any decoded byte.

Input/output arguments use the same pointer/count conventions as `Base64`.
Encoding buffers must be disjoint. Decoding supports disjoint buffers or the
exact same starting address; arbitrary partial overlap is unsupported. Buffers
remain valid for their stated lengths, and metadata references must be separate
from both buffers. A failed encode/decode sets
`written` to zero and leaves every output byte unchanged. Insufficient capacity
does not truncate. Bytes beyond a successful result remain untouched. Results
are counted data without an appended NUL. Empty input succeeds with zero length
without reading input or output storage.

Every five input bytes produce eight digits. Partial groups of one, two, three,
or four bytes use two, four, five, or seven digits, respectively, followed by
six, four, three, or one `=` characters. The final digit's unused low bits are
zero. Complete groups require no `=`. The strict decoder accepts exactly those
canonical shapes, so each byte sequence has one accepted representation.

Include `core/Base32.gf` for selective use. The implementation needs no other
standard source, String, libc, GC, exception allocation, or compiler feature.
Official [RFC test vectors](https://www.rfc-editor.org/rfc/rfc4648.html#section-10)
include `f → MY======`, `fo → MZXQ====`, and `foo → MZXW6===`.
