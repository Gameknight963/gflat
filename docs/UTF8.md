# UTF-8 codec

`core/Text/utf8.gf` provides allocation-free, strict UTF-8 operations in
`std::Text::Utf8`. It can be compiled with `NoStdLib`; it does not depend on libc,
String, GC, or exception support.

| Function | Result |
| --- | --- |
| `IsValidScalar(uint scalar)` | True for Unicode scalar values: U+0000 through U+10FFFF, excluding U+D800 through U+DFFF. |
| `EncodedLength(uint scalar)` | One through four bytes; zero for an invalid scalar. |
| `TryDecode(input, count, &scalar, &consumed)` | Decodes the first complete scalar, reporting its byte length. |
| `TryEncode(scalar, output, capacity, &written)` | Writes one scalar into caller-owned storage. |
| `TryCount(input, count, &scalarCount)` | Validates all input and counts Unicode scalar values. |
| `IsValid(input, count)` | Validates all input bytes. |

Input pointers have type `readonly(char)*`; output storage has type `char*`.
The scalar output is `uint`; byte lengths, capacities, and scalar counts are
`nuint`. Output parameters are pointers to writable caller storage.

`TryDecode`'s scalar and consumed outputs must not overlap each other. Either
may overlap the input: the bytes needed for the scalar are examined before
either output is stored. Result stores can overwrite input bytes, including an
unconsumed suffix, so preserve that suffix separately if it is needed later.
`TryCount` likewise reads the entire input before storing a successful count.
Failure still writes zero to the outputs, even when that changes overlapping
input. Output storage must have its normal scalar alignment and writable size.
For encoding, the `written` variable must not overlap the destination buffer.

All input is counted. U+0000 is an ordinary scalar and does not terminate input.
Encoding does not append a null terminator. Counts describe scalar values,
not grapheme clusters or user-perceived characters.

The decoder rejects overlong encodings, isolated continuation bytes, invalid
leading bytes, surrogate encodings, scalars above U+10FFFF, and incomplete
sequences. It does not replace malformed input. A failed `TryDecode` writes
zero to both outputs; it does not consume an invalid byte sequence. Decoding
one scalar does not validate the following bytes; use `IsValid` or `TryCount`
for full-input validation.

`TryEncode` checks scalar validity and capacity before writing any output byte.
On failure it writes zero to `written` and leaves the output buffer untouched.
`TryCount` writes zero on failure, including when a valid prefix preceded an
invalid sequence. Empty counted input validates successfully and has zero
scalars; decoding one scalar from empty input fails.

The caller supplies readable/writable memory of the requested sizes. No Unicode
normalization, locale behavior, grapheme segmentation, or Unicode category
tables are provided by this codec.
