# Base64

`std/core/Base64.gf` defines `std::Base64`, a caller-buffer codec with no
allocation and no libc dependency. It uses the standard RFC 4648 alphabet
(`A-Z`, `a-z`, `0-9`, `+`, `/`) with required `=` padding. It does not accept
whitespace or URL-safe variants. This is an encoding, not encryption.

- `TryGetEncodedLength(count, ref length)` calculates the required output size,
  returning false on pointer-sized length overflow.
- `TryEncode(data, count, output, capacity, ref written)` writes counted input
  bytes, including zeros. Output is counted ASCII, without a null terminator.
- `TryGetDecodedLength(text, count, ref length)` validates the entire input and
  calculates its decoded size. Nonzero unused padding bits are rejected, so each
  accepted encoding is canonical.
- `TryDecode(text, count, output, capacity, ref written)` validates before
  writing. Malformed input or insufficient capacity leaves output untouched.

Every false result sets the returned length/count to zero. Empty input succeeds
with zero output. Callers provide readable input and writable output storage.
Encoding requires disjoint buffers. Decoding supports disjoint buffers or the
exact same starting address for an in-place operation; other overlapping
regions are unsupported.

`std/core/Base64Text.gf` adds `std::Base64Text::Encode(data, count)` and
`Decode(text, count)`, returning owning `std::String` values. They reserve the
result once and process small stack chunks without allocating an intermediate
whole-size buffer. Decoded strings may contain embedded zeros; use `Length`
rather than a C-string scan. Malformed input, length overflow, and allocation
failure throw. Include the usual core String dependencies for these conveniences.

# Hexadecimal

`std/core/Hex.gf` defines `std::Hex`, with the same counted-buffer conventions:

- `TryGetEncodedLength(count, ref length)` checks multiplication by two for
  pointer-sized overflow.
- `TryEncode(data, count, output, capacity, ref written, uppercase = true)`
  writes two ASCII digits per byte. Pass false for lowercase output.
- `TryGetDecodedLength(text, count, ref length)` validates every digit and
  requires an even input length.
- `TryDecode(text, count, output, capacity, ref written)` accepts mixed letter
  case. Prefixes such as `0x`, whitespace, and separators are rejected.

Failures set the output count to zero and leave the output buffer untouched.
Results have no null terminator. Input/output must be disjoint for encoding;
decoding additionally supports the exact same starting address. Empty input
succeeds. `std/core/HexText.gf` adds owning `std::HexText::Encode(data, count,
uppercase = true)` and `Decode(text, count)` conveniences. Decoded strings retain
embedded zeros and report malformed input through an exception.

# Component percent encoding

`std/core/PercentEncoding.gf` defines `std::PercentEncoding`. This encodes
component **data**, not an entire URI: only unreserved ASCII letters, digits,
`-`, `.`, `_`, and `~` pass through. Other bytes become uppercase `%HH` escapes,
following [RFC 3986 sections 2.1–2.4](https://www.rfc-editor.org/rfc/rfc3986.html#section-2.1).
Existing percent escapes are treated as data and encoded again.

- `TryGetEncodedLength(data, count, ref length)` checks the accumulated length
  for overflow.
- `TryEncode(data, count, output, capacity, ref written)` requires disjoint
  buffers and writes no terminator.
- `TryGetDecodedLength(text, count, ref length)` validates all percent escapes.
- `TryDecode(text, count, output, capacity, ref written)` accepts either hex
  letter case. It decodes exactly once and preserves literal `+`; it is not an
  HTML form decoder.

All four methods are `nothrow` and allocation-free. A failure sets the returned
count to zero; encoding/decoding failures leave output untouched. Decoding
supports disjoint or exact same-address buffers. Raw and decoded bytes may
include zeros; no UTF-8 validation or URI parsing occurs. Supply UTF-8 bytes
explicitly when encoding Unicode text.

`PercentEncodingText.gf` separately adds owning `PercentEncodingText::Encode`
and `Decode` conveniences. These reserve the output String once, preserve
binary zeros, and throw on invalid percent syntax or allocation failure.
