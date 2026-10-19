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
