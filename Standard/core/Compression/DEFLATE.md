# Raw DEFLATE

`Standard::Compression::Deflate::Decompress` decodes a single RFC 1951 raw stream into caller-owned bytes. It supports stored, fixed-Huffman and dynamic-Huffman blocks. It does not allocate, retain buffers, invoke user callbacks, use a collector, or require a native compression runtime. This first API is one-shot: it has no resumable state or streaming history object.

```csharp
using Standard::Compression;

int main() nothrow
{
    byte[5] compressed = [0x4b, 0x4c, 0x4a, 0x06, 0x00];
    byte[3] output;
    nuint consumed = 0;
    nuint written = 0;
    Deflate::Status status = Deflate::Decompress(compressed, 5, output, 3, ref consumed, ref written);
    // Done, consumed=5, written=3; output contains abc without a terminator.
    if (status != Deflate::Status::Done || consumed != 5 || written != 3) return 1;
    return 0;
}
```

Counts describe bytes, including zero and high bytes. `source` must provide `count` readable bytes, and `output` must provide `capacity` writable bytes. These ranges must not overlap. The two count outputs must be separate initialized values, disjoint from both buffers and each other. Callers retain ownership and keep all storage alive until the call returns. Zero input count does not dereference source. Zero output capacity does not dereference output, and can still successfully decode a stream with no output bytes.

| Status | Meaning |
|---|---|
| `Done` | The final block ended successfully. |
| `NeedInput` | The supplied input ended before a required field, code, or stored payload completed. |
| `OutputTooSmall` | The next complete literal, stored block, or match does not fit in remaining output capacity. |
| `InvalidData` | An invalid header, code tree, code, repeat, length complement, or backward distance was found. |

For every result, `written` reports the completed output prefix and `consumed` reports source bytes examined. They are not resume tokens. On failure the output prefix may already have changed; later output bytes remain untouched. A stored block is copied only after its full payload and output capacity have been checked; a match is copied only after its complete length and distance have been validated. The first encountered condition determines the status. A capacity failure does not establish that the rest of the stream is valid. Retry from the complete input into a fresh output pass.

On `Done`, `consumed` includes the byte containing the end-of-block code. Unused bits in that final byte are ignored regardless of their values. Whole trailing bytes are not prefetched or consumed, so a caller can locate a following container checksum. This is not checksum validation, ZIP parsing, gzip decoding, or zlib-wrapper decoding. A caller expecting one exact raw stream can require `consumed == count`.

Backward matches may cross earlier blocks and overlap their own newly produced output. Distances must remain within the output already produced and at most 32,768 bytes. No preset dictionary is supported by this API. The output capacity is an application-selected bound; the decoder does not choose or grow it.

Code trees use canonical decoding with bounded inline tables. Oversubscribed trees are rejected. Code-length trees must be complete and nonempty; literal/length and distance trees also permit the one-symbol, one-bit form. Literal/length trees require symbol 256. An empty distance alphabet is accepted for literal-only data, but trying to decode a match from it is invalid. Code-length repeat runs can cross the literal/distance boundary and must fit the combined alphabet.

The HDIST field accepts all 1-32 lengths described by RFC 1951. Reserved distance symbols 30 and 31 are rejected if used; the header may contain unused reserved slots. This intentionally differs from zlib's usual limit of 30 declared distance lengths. The host .NET decompressor is therefore an independent oracle for its supported subset, not the authority for every RFC-valid declaration. The decoder also tolerates the duplicate length-258 representation formed by length code 284 plus extra value 31, as common zlib implementations do. The RFC table assigns that code to 227-257 and assigns 258 to code 285; this tolerance does not make the API a strict canonical-encoding validator. Tests use a separately constructed fixture and expected bytes for this policy, rather than treating host .NET acceptance as its definition.

The decoder keeps two Huffman trees in its caller frame and one while reading a dynamic header. Each has 608 bytes of code-count/symbol arrays plus scalar metadata; dynamic length storage is at most 320 bytes. Scratch is bounded independently of input/output sizes, with no large table or 32 KiB history allocation. Actual frame layout remains a compiler/target detail.

Specification: [RFC 1951](https://www.rfc-editor.org/rfc/rfc1951), especially sections 3.1.1 and 3.2.2-3.2.7. Implementation is original; no third-party decoder source was copied.
