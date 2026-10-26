# Base64

`std/core/Encoding/Base64.gf` defines `std::Encoding::Base64`, a caller-buffer codec with no
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

For Base64, Base64Url, Hex, and PercentEncoding, the scalar `length` and `written`
variables must occupy separate storage from the input and output byte buffers.
In-place decoding support applies to the byte buffers, not these count outputs:
storing a count into encoded or decoded bytes would overwrite their contents.

`std/core/Encoding/Base64Text.gf` adds `std::Encoding::Base64Text::Encode(data, count)` and
`Decode(text, count)`, returning owning `std::Text::String` values. They reserve the
result once and process small stack chunks without allocating an intermediate
whole-size buffer. Decoded strings may contain embedded zeros; use `Length`
rather than a C-string scan. Malformed input, length overflow, and allocation
failure throw. Include the usual core String dependencies for these conveniences.

For chunked input, [Base64Encoder](BASE64_ENCODER.md) wraps an `ITextWriter` and
emits the same padded encoding without a whole-input buffer. `File::CopyTo` can
feed it directly; call `Complete()` to emit any final padding. It does not close
or flush the borrowed destination.

[Base64Decoder](BASE64_DECODER.md) accepts encoded chunks through the same
writer interface and sends decoded bytes to its borrowed destination. It uses
the same strict input rules; unlike whole-buffer `TryDecode`, a later input or
output failure can occur after earlier chunks have already reached the sink.

# URL-safe Base64

`std/core/Encoding/Base64Url.gf` defines `std::Encoding::Base64Url`, a separate caller-buffer codec
using the RFC 4648 section 5 alphabet (`-` and `_` instead of `+` and `/`). This
API explicitly selects the **unpadded** profile: it emits no `=` and rejects
padding on input. It also rejects whitespace, standard-alphabet `+`/`/`, and
nonzero unused bits in a final two- or three-character group. There is one
accepted encoding for each byte sequence. Input lengths congruent to one modulo
four cannot encode complete bytes and are rejected.

The four `nothrow` methods have the same argument order as `Base64`:

- `TryGetEncodedLength(count, ref length)` checks pointer-sized size overflow.
- `TryEncode(data, count, output, capacity, ref written)` emits counted ASCII.
- `TryGetDecodedLength(text, count, ref length)` validates the entire input.
- `TryDecode(text, count, output, capacity, ref written)` validates input and
  capacity before writing any output.

Every failure sets the output count to zero and leaves the output byte buffer
untouched. Empty input succeeds without reading input or writing output. No
terminator is added; binary zeros are ordinary data. Encoding requires disjoint
buffers; decoding also supports the exact same starting address. Other overlap
is unsupported. Count output variables must remain separate from both buffers.
The codec allocates nothing and requires neither `String`, libc, nor `Base64`.

For example, the bytes `FB FF FF` encode to `-___`; a single ASCII `f` encodes
to `Zg`. `Zg==` and `Zh` are both rejected: the first has padding, and the
second has nonzero unused bits. This is an encoding, not encryption.

The alphabet and explicitly omitted padding follow
[RFC 4648 section 5](https://www.rfc-editor.org/rfc/rfc4648.html#section-5);
canonical unused bits follow
[section 3.5](https://www.rfc-editor.org/rfc/rfc4648.html#section-3.5).

`std/core/Encoding/Base64UrlText.gf` separately provides owning
`Base64UrlText::Encode(data, count)` and `Decode(text, count)` conveniences.
They use this same strictly unpadded profile, reserve the result once, and
process small stack chunks. Decoded strings preserve binary zeros and own an
independent, null-terminated buffer; `Length` excludes that terminator. Empty
input returns an empty String without allocating result storage. Invalid input,
length overflow, and allocation failure throw. Input must remain readable and
unchanged throughout the call; no source pointer is retained. These optional
helpers require the normal String dependencies, while `Encoding/Base64Url.gf` remains
usable without String or libc.

```csharp
using std::Text;
using std; using std::Encoding;

int main()
{
    String original = s"A\0B";
    String encoded = Base64UrlText::Encode(original.Data, original.Length);
    String decoded = Base64UrlText::Decode(encoded.Data, encoded.Length);
    // Counted input preserves the embedded null; a C-string scan would not.
    if (!encoded.StartsWith("QQBC") || encoded.Length != 4) return 1;
    if (!decoded.Equals(&original)) return 2;
    return 0;
}
```

# Hexadecimal

`std/core/Encoding/Hex.gf` defines `std::Encoding::Hex`, with the same counted-buffer conventions:

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
succeeds. `std/core/Encoding/HexText.gf` adds owning `std::Encoding::HexText::Encode(data, count,
uppercase = true)` and `Decode(text, count)` conveniences. Decoded strings retain
embedded zeros and report malformed input through an exception.

The optional `std/core/Encoding/hex_writer.gf` adapter adds
`WriteHex(this ITextWriter* output, readonly(char)* data, nuint count,
bool uppercase = true)`. It writes two digits per input byte with no prefix,
separator, whitespace, newline, or terminator. Uppercase is the default, matching
`Hex::TryEncode` and `HexText::Encode`; pass `false` for lowercase.

```csharp
using std::Text;
using std; using std::Encoding;
int main()
{
    char[3] bytes = [(char)0, (char)0xab, (char)0xff];
    String text = new();
    ITextWriter* output = &text;
    output.WriteHex(bytes, 3);
    if (text.Length != 6 || text.Data[0] != '0' || text.Data[2] != 'A'
        || text.Data[5] != 'F') return 1;
    return 0;
}
```

Input is counted binary data, including embedded NUL bytes. Empty input makes
no writer calls. The adapter checks the total encoded length for pointer-sized
overflow before reading input or calling the writer, throwing `Exception` with
`Hex length overflow` on failure. It then reuses `Hex::TryEncode` for chunks of
at most 128 input bytes, writing each encoded chunk from a 256-byte stack buffer.
It allocates no intermediate owning string or other heap storage. The writer
can allocate according to its own contract.

The writer is borrowed and is never closed or destroyed. Writer exceptions
propagate and can leave an output prefix; retrying the entire call can duplicate
that prefix. Input must remain valid and unchanged throughout the call, separate
from writer state and destination storage. Each `Write` consumes its supplied
span immediately and must not retain its pointer to temporary stack storage.
Include `Encoding/hex_writer.gf` with `Encoding/Hex.gf` and the normal text/exception contracts;
the adapter uses no `String` type and makes no libc calls. Its writer and the
language's exception runtime retain their existing dependency requirements.

# Component percent encoding

`std/core/Encoding/PercentEncoding.gf` defines `std::Encoding::PercentEncoding`. This encodes
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

`Encoding/PercentEncodingText.gf` separately adds owning `PercentEncodingText::Encode`
and `Decode` conveniences. These reserve the output String once, preserve
binary zeros, and throw on invalid percent syntax or allocation failure.
