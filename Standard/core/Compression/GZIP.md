# Gzip members

`Standard::Compression::Gzip::DecompressMember` decodes and validates one gzip
member into caller-owned bytes. It uses bounded stack scratch and caller buffers.

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

## Validation

The operation accepts DEFLATE members with optional extra bytes, terminated names
and comments, and header checksums. It checks:

- Reserved flags
- The header checksum when present
- The payload CRC32
- The uncompressed size modulo 2^32

Extra bytes are opaque, and names and comments serve as metadata. Framing follows
[RFC 1952 sections 2.2–2.3](https://www.rfc-editor.org/rfc/rfc1952.txt).
For a file containing several members, call the operation for each member.

## Buffers

Source supplies `count` readable bytes and output supplies `capacity` writable
bytes. The ranges must be disjoint. Both count outputs must be initialized,
separate from each other and both buffers. Keep all storage alive until return.
Zero input count or output capacity leaves its corresponding pointer untouched.
Use valid dummy storage for an empty buffer. Empty output still requires a
complete compressed body and footer.

## Results

| Status | Meaning |
| --- | --- |
| `Done` | One member passed framing, decompression, and integrity checks |
| `NeedInput` | A required header field, body, or footer was incomplete |
| `OutputTooSmall` | The raw decoder's next complete output unit exceeds capacity |
| `InvalidData` | Header/body validation or a checksum/size comparison failed |

`written` reports produced bytes, and `consumed` reports structurally consumed
source bytes. A failure can leave partial output. A truncated or invalid footer
can leave all decoded bytes present. Treat output as validated only on `Done`.
The first encountered condition determines the result. A capacity failure leaves
the remaining member unvalidated.

Retry from complete input in a fresh output pass. Bytes beyond `written` remain
unchanged.

## Consumption

- Fewer than ten header bytes consumes and writes zero bytes. A complete header
  is consumed before checking identity, method, and flags
- Incomplete XLEN or FHCRC fields remain unconsumed. A complete XLEN is consumed
  before checking for all extra bytes. Complete extra fields are skipped together
- Names and comments consume examined bytes, including a terminator when present.
  An unterminated field consumes all available bytes
- A complete FHCRC is consumed before comparison. Header failures write no output
- Body failures add the raw decoder's consumed/written counts to the header offset
- Fewer than eight footer bytes returns `NeedInput` at the body's end. A complete
  footer is consumed before comparing checksum and size

On `Done`, consumed includes the footer and stops before the next member or
trailing bytes. Raw DEFLATE padding bits are ignored. Require `consumed == count`
for exactly one member, or loop over adjacent members with an aggregate output
limit. Trailing bytes require separate validation.

Input count bounds header scanning, and capacity bounds output. Work includes
at most two header passes, DEFLATE decoding, and one checksum pass over output.
The decoder retains no buffers between calls. Raw DEFLATE's documented
[code-tree tolerances](DEFLATE.md#distances-and-code-trees) also apply here.
