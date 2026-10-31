# Padded Base32

`Standard::Encoding::Base32` encodes bytes with the uppercase `A`–`Z`, `2`–`7`
alphabet and `=` padding from [RFC 4648](https://www.rfc-editor.org/rfc/rfc4648.html#section-6).

```csharp
using Standard;
using Standard::Encoding;

int main()
{
    char[8] encoded;
    char[3] decoded;
    nuint written = 0;
    if (!Base32::TryEncode("foo", 3, encoded, 8, ref written) || written != 8) return 1;
    for (nuint i = 0; i < 8; i++) if (encoded[i] != "MZXW6==="[i]) return 2;
    if (!Base32::TryDecode(encoded, 8, decoded, 3, ref written) || written != 3) return 3;
    for (nuint i = 0; i < 3; i++) if (decoded[i] != "foo"[i]) return 4;
    return 0;
}
```

| Operation | Result |
| --- | --- |
| `TryGetEncodedLength(count, ref length)` | Padded output length. Overflow returns false and zero. |
| `TryEncode(data, count, output, capacity, ref written)` | Encoded bytes |
| `TryGetDecodedLength(text, count, ref length)` | Decoded length after validating the input |
| `TryDecode(text, count, output, capacity, ref written)` | Decoded bytes after validating input and capacity |

All lengths count bytes. Results omit a NUL terminator. Empty input succeeds.
Failure sets the result count to zero and preserves the output buffer.

Encoding requires separate input and output buffers. Decoding also permits
in-place output at the input's starting address. Keep count outputs separate
from both buffers.

Five input bytes produce eight characters. Partial groups use the following padding:

| Remaining input bytes | Alphabet characters | `=` characters |
| --- | --- | --- |
| 1 | 2 | 6 |
| 2 | 4 | 4 |
| 3 | 5 | 3 |
| 4 | 7 | 1 |

The decoder requires this padding and zero unused bits. It rejects lowercase,
whitespace, and characters outside the alphabet. RFC examples include
`f → MY======`, `fo → MZXQ====`, and `foo → MZXW6===`.

For chunked input, use [Base32Encoder](BASE32_ENCODER.md) or
[Base32Decoder](BASE32_DECODER.md).

## Owning results

`Base32Text::Encode(data, count)` and `Decode(text, count)` return `String`
values. Invalid text throws `Exception`. Allocation failure throws
`BadAllocException`. Decoded strings can contain NUL bytes, so pass their
`Length` to counted consumers.

```csharp
using Standard::Text;
using Standard;
using Standard::Encoding;

int main()
{
    String encoded = Base32Text::Encode("a\0b", 3);
    String decoded = Base32Text::Decode(encoded.Data, encoded.Length);
    if (!decoded.Equals("a\0b", 3)) return 1;
    return 0;
}
```

Sources: `core/Encoding/Base32.gf`, `core/Encoding/Base32Text.gf`
