# Zlib streams

`Standard::Compression::Zlib::Decompress` decodes and validates one zlib stream
into caller-owned bytes, using bounded stack scratch and caller buffers.

```csharp
using Standard::Compression;

int main() nothrow
{
    byte[8] source = [120, 1, 3, 0, 0, 0, 0, 1];
    byte output = 0; // Valid storage for an empty result.
    nuint consumed = 0;
    nuint written = 0;
    Zlib::Status status = Zlib::Decompress(source, 8, &output, 0, ref consumed, ref written);
    if (status != Zlib::Status::Done || consumed != 8 || written != 0) return 1;
    return 0;
}
```

## Validation

The wrapper checks method 8, the two-byte header check, the declared 256–32768
byte history window, and the final big-endian Adler32 checksum. A smaller window
limits match distances while capacity bounds total output. Compression-level
hints leave decoding unchanged.

The framing follows [RFC 1950 sections 2.2–2.3](https://www.rfc-editor.org/rfc/rfc1950.txt).
A stream requiring a preset dictionary returns `UnsupportedDictionary` after
reading its complete dictionary identifier.

## Buffers

Source provides `count` readable bytes, and output provides `capacity` writable
bytes. The ranges must be disjoint. Both count outputs must be initialized,
separate from each other and the buffers. Keep all storage alive until return.
A zero count leaves the corresponding buffer untouched. Use valid dummy storage
for an empty buffer. Empty output still requires a complete body and checksum.

## Results

| Status | Meaning |
| --- | --- |
| `Done` | One stream passed header, body, declared-window, and checksum checks |
| `NeedInput` | A required header field, body, or checksum was incomplete |
| `OutputTooSmall` | The next complete raw-decoder output unit exceeds capacity |
| `InvalidData` | Header/body/window validation or checksum comparison failed |
| `UnsupportedDictionary` | A valid header requests a preset dictionary |

`written` reports produced output, and `consumed` reports structurally consumed
source bytes. Failures can leave partial output. A truncated or invalid checksum
can leave all decoded bytes present. Treat output as validated only on `Done`.
A capacity failure leaves the remaining input unvalidated.

Retry from complete input in a fresh output pass. Bytes beyond `written` remain
unchanged.

## Consumption

- Fewer than two header bytes consumes and writes zero bytes. A complete header
  is consumed before validation. Invalid method, window, or header check returns
  `InvalidData` with consumed=2
- With FDICT set, fewer than four identifier bytes returns `NeedInput` with
  consumed=2. A complete identifier returns `UnsupportedDictionary` with
  consumed=6 and written=0, before inspecting the body
- Body results include raw-decoder counts after the header. A match exceeding the
  declared window is rejected after decoding its complete distance, before
  copying the match or checking output capacity
- Fewer than four checksum bytes returns `NeedInput` at the body end. A complete
  checksum is consumed before comparison
- `Done` includes the checksum. Padding bits in the final DEFLATE byte are ignored,
  and subsequent whole bytes remain unconsumed and unvalidated

Require `consumed == count` when expecting exactly one stream. An application can
also decode adjacent streams using the returned offset. Input count bounds source
access, and capacity bounds output. Checksum verification makes one pass over the
produced bytes.

Raw DEFLATE's [code-tree tolerances](DEFLATE.md#distances-and-code-trees) apply.
The zlib wrapper supplies its declared history limit to the shared decoder.
