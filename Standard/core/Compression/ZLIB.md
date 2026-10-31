# Zlib streams

`Standard::Compression::Zlib::Decompress` decodes and validates one zlib stream into caller-owned bytes. It allocates nothing, retains no buffers, and needs no GC, exception runtime, or native compression library. Preset dictionaries are explicitly unsupported by this operation.

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

The wrapper validates method 8, the two-byte header check, the declared 256-32768 byte history window, and the final big-endian Adler32 checksum. Compression-level hints do not change decoding. A declared smaller window limits match distances, not total output size. The format and dictionary identifier follow [RFC 1950 sections 2.2-2.3](https://www.rfc-editor.org/rfc/rfc1950.txt). This API supports streams without preset dictionaries and reports the unsupported feature distinctly when a complete dictionary header is supplied.

Source provides `count` readable bytes and output provides `capacity` writable bytes. The ranges must not overlap. Both count outputs are independent initialized values, disjoint from each other and the buffers. All storage stays caller-owned and alive until return; no pointers are retained. A zero count does not dereference the corresponding buffer. Use valid dummy storage when a buffer has no bytes. Empty output still needs a complete compressed body and checksum.

| Status | Meaning |
|---|---|
| `Done` | One complete stream passed header, body, declared-window, and checksum checks. |
| `NeedInput` | A complete required header field, body, or checksum was unavailable. |
| `OutputTooSmall` | The next complete raw-decoder output unit would exceed capacity. |
| `InvalidData` | Header/body/window validation or checksum comparison failed. |
| `UnsupportedDictionary` | A valid header requires a preset dictionary, which this API cannot supply. |

On every return, `written` reports output produced and `consumed` reports structurally consumed source bytes. They are not resume tokens. Failures can leave a partial prefix, and a checksum failure or truncated checksum can leave the complete decoded output present. Only `Done` establishes integrity. A capacity failure does not promise validity of the remaining input. Retry from complete source in a fresh output pass. Bytes after `written` remain unchanged.

Consumption is field-based:

- Fewer than two header bytes consumes zero and writes zero. A complete header is consumed before validating it; invalid method, window information, or header check returns `InvalidData` with consumed=2.
- If FDICT is set, fewer than four identifier bytes returns `NeedInput` with consumed=2. A complete identifier is consumed before `UnsupportedDictionary`: consumed=6, written=0. No body is inspected, and no identifier is treated as an implicitly known dictionary.
- Body results include the raw decoder's consumed/written counts after the header. A match exceeding the declared window is rejected after its complete encoded distance, before copying that match or checking whether it fits capacity. Earlier output remains present.
- Fewer than four checksum bytes returns `NeedInput` at the body end without consuming checksum bytes. A complete checksum is consumed before comparison, even when invalid.
- `Done` includes the four-byte checksum. The raw decoder ignores unused final-byte padding and does not consume subsequent whole bytes; trailing bytes are untouched and unvalidated.

Callers expecting exactly one stream can require `consumed == count`. Adjacent application-supplied streams can be decoded explicitly using the returned offset, but this is not a whole-file concatenation convention. Capacity is an application-selected output bound; input count bounds source access. The checksum requires one pass over produced bytes. No stream continuation, preset dictionary storage, allocator policy, or separate history buffer is hidden in the wrapper.

Raw DEFLATE's existing documented code-tree tolerances are unchanged. Its public operation still permits backward distances through 32768; only the zlib wrapper requests the smaller declared limit through an internal implementation entry.
