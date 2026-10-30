# UTF-8 codec

`core/Text/utf8.gf` provides allocation-free, strict UTF-8 operations in
`Standard::Text::Utf8`. It can be compiled with `NoStdLib`; it does not depend on libc,
String, GC, or exception support.

| Function | Result |
| --- | --- |
| `IsValidScalar(uint scalar)` | True for Unicode scalar values: U+0000 through U+10FFFF, excluding U+D800 through U+DFFF. |
| `EncodedLength(uint scalar)` | One through four bytes; zero for an invalid scalar. |
| `TryDecode(input, count, &scalar, &consumed)` | Decodes the first complete scalar, reporting its byte length. |
| `TryDecodeLast(input, count, &scalar, &consumed)` | Decodes the final complete scalar, reporting its byte length. |
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

`TryDecodeLast` examines at most the final four input bytes to locate and
validate the final scalar. It does not validate the preceding bytes; a malformed
prefix followed by a valid final scalar can succeed. Empty input and malformed
or incomplete final scalars fail with both outputs zero. The output alignment
and overlap rules are the same as for `TryDecode`: result stores can overwrite
the retained prefix, so use separate metadata if that prefix is still needed.

To remove a final scalar from a counted view, decode it and subtract its byte
length. Repeating this operation walks scalar boundaries backward without
building an index. Scalar boundaries do not represent grapheme clusters.

```csharp
int main()
{
    char[5] text = ['A', (char)0xf0, (char)0x9f, (char)0x98, (char)0x80];
    nuint length = 5;
    uint scalar = 0u;
    nuint consumed = 0;
    if (!Standard::Text::Utf8::TryDecodeLast(text, length, &scalar, &consumed)) return 1;
    if (scalar != 0x1f600u || consumed != (nuint)4) return 2;
    length -= consumed;
    if (length != (nuint)1 || text[0] != 'A') return 3;
    return 0;
}
```

`TryEncode` checks scalar validity and capacity before writing any output byte.
On failure it writes zero to `written` and leaves the output buffer untouched.
`TryCount` writes zero on failure, including when a valid prefix preceded an
invalid sequence. Empty counted input validates successfully and has zero
scalars; decoding one scalar from empty input fails.

The caller supplies readable/writable memory of the requested sizes. No Unicode
normalization, locale behavior, grapheme segmentation, or Unicode category
tables are provided by this codec.
