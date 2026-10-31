# Gzip members

`Standard::Compression::Gzip::DecompressMember` decodes and validates one gzip member into caller-owned bytes. It allocates nothing, retains no buffers, and needs no GC, exception runtime, or native compression library.

```csharp
using Standard::Compression;

int main() nothrow
{
    byte[20] source = [31, 139, 8, 0, 0, 0, 0, 0, 0, 255, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    byte output = 0; // Valid storage even though this member has no output.
    nuint consumed = 0;
    nuint written = 0;
    Gzip::Status status = Gzip::DecompressMember(source, 20, &output, 0, ref consumed, ref written);
    if (status != Gzip::Status::Done || consumed != 20 || written != 0) return 1;
    return 0;
}
```

The operation accepts DEFLATE members, optional extra bytes, terminated names/comments, and optional header checksums. It checks reserved flags, header checksum when supplied, payload CRC32, and uncompressed size modulo 2^32. Extra bytes are opaque; metadata does not alter binary output or cause filesystem access. This framing follows [RFC 1952 sections 2.2-2.3](https://www.rfc-editor.org/rfc/rfc1952.txt). A gzip file can contain several adjacent members, so this deliberately named operation does not claim to validate an entire file.

Source must supply `count` readable bytes and output `capacity` writable bytes. Those ranges must not overlap. The two count outputs must be initialized independent values, disjoint from each other and both buffers. All storage remains caller-owned and alive until return. Zero input count or output capacity does not dereference the corresponding pointer; use valid dummy storage when no bytes are needed. An empty output still requires a complete compressed body and footer.

| Status | Meaning |
|---|---|
| `Done` | One complete member passed framing, decompression, and integrity checks. |
| `NeedInput` | A complete required header field, body, or footer was unavailable. |
| `OutputTooSmall` | The raw decoder's next complete output unit would exceed capacity. |
| `InvalidData` | Header/body validation or a checksum/size comparison failed. |

Every status can leave an output prefix. In particular, a truncated or invalid footer can leave the complete decoded output present: only `Done` establishes integrity. The first encountered condition determines the result. A capacity failure does not establish that the rest of the member is valid.

`written` reports produced bytes. `consumed` reports input consumed by structural fields and the raw decoder, including opaque fields skipped. Neither is a resume token. A retry starts from complete input in a fresh output pass. Bytes beyond `written` remain unchanged.

Consumption on failures is deliberately field-based:

- Fewer than ten header bytes: zero consumed and written. Once all ten exist, they are consumed before checking identity, method, and flags.
- An incomplete two-byte XLEN or FHCRC field is not consumed. A complete XLEN is consumed before checking that all extra bytes exist; incomplete extra bytes remain unconsumed. A complete extra field is skipped together.
- Names and comments consume examined bytes, including the terminating zero when present. An unterminated field consumes all available bytes.
- A complete FHCRC is consumed before its comparison. Header failures produce no output.
- Body failures add the raw decoder's exact consumed/written counts to the header position.
- Fewer than eight footer bytes: `NeedInput` with consumed at the body's end; no footer bytes are consumed. A complete footer is consumed before comparing either checksum or size.

On `Done`, consumed includes the eight-byte footer and stops before the next member or any trailing bytes. The underlying raw decoder ignores unused final-byte padding and does not prefetch subsequent whole bytes. Callers can require `consumed == count` for exactly one member, or loop explicitly over adjacent members while enforcing an aggregate output limit. Arbitrary trailing bytes are not validated by a successful member call.

Input count bounds header scanning; capacity bounds produced output. Work includes at most two header passes, DEFLATE decoding, and a checksum pass over produced bytes. The wrapper adds no unbounded scratch allocation and does not select buffer sizes, grow buffers, or retain streaming state. The raw decoder's documented code-tree tolerances remain unchanged.
