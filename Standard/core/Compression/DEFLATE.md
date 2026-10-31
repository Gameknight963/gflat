# Raw DEFLATE

`Standard::Compression::Deflate::Decompress` decodes one RFC 1951 raw stream into
caller-owned storage. It supports stored, fixed-Huffman, and dynamic-Huffman
blocks. Each call decodes from the start using bounded stack scratch space.

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

## Buffers

- `source` provides `count` readable bytes, and `output` provides `capacity`
  writable bytes. The ranges must be disjoint
- `consumed` and `written` are separate initialized values, disjoint from the
  buffers and each other
- Keep all storage alive until return
- Zero input count or output capacity leaves the corresponding pointer untouched.
  Zero capacity can decode an empty stream

Counts include zero and high bytes. The decoder uses caller-selected capacity
and performs no allocation.

## Results

| Status | Meaning |
| --- | --- |
| `Done` | The final block ended successfully |
| `NeedInput` | Input ended inside a required field, code, or stored payload |
| `OutputTooSmall` | The next complete literal, stored block, or match exceeds capacity |
| `InvalidData` | An invalid header, code tree, code, repeat, length complement, or distance was found |

`written` reports the completed output prefix. `consumed` reports examined source
bytes. A failure can leave output in that prefix. Later output bytes remain
untouched. Retry from the start with complete input and a fresh output pass.

A stored block is copied after checking its complete payload and capacity. A
match is copied after validating its complete length and distance. The first
encountered failure determines the status. An `OutputTooSmall` result leaves the
remaining input unvalidated.

On `Done`, `consumed` includes the byte containing the end-of-block code. Padding
bits in that byte are ignored. Whole trailing bytes remain unconsumed, allowing a
caller to locate a container checksum. Require `consumed == count` when expecting
exactly one raw stream. Use [gzip](GZIP.md) or [zlib](ZLIB.md) for wrapper validation.

## Distances and code trees

Backward matches can cross blocks and overlap their own output. Distances must
fit within already produced output and the 32,768-byte history limit. This API
accepts streams that use no preset dictionary.

Code trees use canonical decoding with bounded inline tables:

- Oversubscribed trees are rejected
- Code-length trees must be complete and nonempty
- Literal/length and distance trees also permit a one-symbol, one-bit tree
- Literal/length trees require symbol 256
- An empty distance alphabet is valid for literal-only data. A match using it is
  invalid
- Repeat runs can cross the literal/distance boundary and must fit the combined
  alphabet

HDIST accepts 1–32 lengths from RFC 1951. Reserved distance symbols 30 and 31 are
rejected when used. Unused reserved slots in a header are accepted. This differs
from zlib's usual limit of 30 declared distance lengths.

Like common zlib implementations, this decoder accepts length code 284 with
extra value 31 as another encoding of length 258. RFC 1951 assigns 258 to code
285. Tests cover this tolerance with an independently constructed fixture.

## Scratch storage

Two Huffman trees stay in the call frame, with a third used while reading a
dynamic header. Each has 608 bytes of code-count and symbol arrays plus scalar
metadata. Dynamic length storage is at most 320 bytes. Scratch size is independent
of input and output lengths. Final frame layout depends on the compiler and target.

Specification: [RFC 1951](https://www.rfc-editor.org/rfc/rfc1951), sections 3.1.1
and 3.2.2–3.2.7.
