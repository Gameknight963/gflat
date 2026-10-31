# Base64

`Standard::Encoding::Base64` uses the [RFC 4648](https://www.rfc-editor.org/rfc/rfc4648.html)
alphabet `A`–`Z`, `a`–`z`, `0`–`9`, `+`, `/`, with `=` padding.

Base64, Base64Url, Hex, and PercentEncoding expose four buffer operations:

| Operation | Result |
| --- | --- |
| `TryGetEncodedLength(count, ref length)` | Required output size, or false and zero on overflow |
| `TryEncode(data, count, output, capacity, ref written)` | Encoded bytes |
| `TryGetDecodedLength(text, count, ref length)` | Decoded size after validating input |
| `TryDecode(text, count, output, capacity, ref written)` | Decoded bytes after validating input and capacity |

Counts and capacities measure bytes. Results omit a terminator. Empty input
succeeds. Any failure sets the output count to zero and preserves the output
buffer. Encoding requires separate input and output. Decoding also permits
output at the input's exact starting address. Keep scalar count outputs separate
from both buffers.

Base64 decoding requires canonical padding and zero unused bits. Whitespace
and URL-safe `-`/`_` are rejected.

`Base64Text::Encode(data, count)` and `Decode(text, count)` return owning Strings.
The corresponding `Base64UrlText`, `HexText`, and `PercentEncodingText` helpers
follow their codecs' rules. They throw for malformed input, size overflow, or
allocation failure. Decoded results can contain NULs, so use `Length`.

For chunks, use [Base64Encoder](BASE64_ENCODER.md) and
[Base64Decoder](BASE64_DECODER.md). Call `Complete()` to finish a stream. A
streaming decoder can emit a prefix before discovering invalid later input.

# URL-safe Base64

`Base64Url` uses `-` and `_` in place of `+` and `/` and omits padding. Decoding
requires this unpadded alphabet and zero unused bits. A length congruent to one
modulo four is invalid.

For example, `FB FF FF` encodes to `-___`, and ASCII `f` encodes to `Zg`.
`Zg==` fails because it contains padding. `Zh` fails because its unused bits
are nonzero.

```csharp
using Standard::Text;
using Standard;
using Standard::Encoding;

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

`Hex::TryEncode(data, count, output, capacity, ref written, uppercase = true)`
writes two digits per byte. Pass `false` for lowercase. Decoding accepts either
case and requires an even number of digits. Prefixes, separators, and whitespace
are invalid.

`HexText::Encode(data, count, uppercase = true)` and `Decode(text, count)` return
owning Strings. The `WriteHex(data, count, uppercase = true)` extension sends
encoded bytes directly to an `ITextWriter`:

```csharp
using Standard::Text;
using Standard;
using Standard::Encoding;

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

`WriteHex` checks output-size overflow before writing. It sends bounded chunks
from a 256-byte stack buffer. Writer failure can leave a prefix. Keep input
separate from any storage the writer may modify.

# Component percent encoding

`PercentEncoding` escapes URI component bytes as uppercase `%HH`. ASCII letters,
digits, `-`, `.`, `_`, and `~` pass through. Existing percent escapes are escaped
again. Supply UTF-8 bytes when encoding Unicode text.

Its encoded-length operation takes `(data, count, ref length)` because length
depends on byte values. Decoding accepts either hex case, decodes once, and
preserves a literal `+`. Thus `+` differs from form-encoded space.

These rules follow [RFC 3986 sections 2.1–2.4](https://www.rfc-editor.org/rfc/rfc3986.html#section-2.1).

Sources are in `core/Encoding`: each codec has a matching `.gf` file, owning
helpers use the `Text.gf` suffix, and writer hex output is in `hex_writer.gf`.
