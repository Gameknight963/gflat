# Convert Base64 through spans

Optional span extensions provide `TryEncodeBase64`, `TryDecodeBase64`, and
`TryGetBase64DecodedLength`. The URL-safe forms use `Base64Url` in those names.
Free functions with the same names are available in `Standard::Encoding`.

```csharp
using Standard::Encoding;
using Standard::Memory;

int main()
{
    byte[2] input = [(byte)251, (byte)255];
    char[3] text;
    nuint written = 0;
    if (!new Span<readonly(byte)>(&input[0], 2).TryEncodeBase64Url(new Span<char>(&text[0], 3), ref written)) return 1;
    if (written != 3 || text[0] != '-' || text[1] != '_' || text[2] != '8') return 2;
    byte[2] decoded;
    if (!new Span<readonly(char)>(&text[0], 3).TryDecodeBase64Url(new Span<byte>(&decoded[0], 2), ref written)) return 3;
    if (written != 2 || decoded[0] != (byte)251 || decoded[1] != (byte)255) return 4;
    return 0;
}
```

Encoding accepts byte or char input spans and writes a char span. Decoding
accepts a readonly char span and writes a byte or char span. Ordinary Base64
uses the `+`/`/` alphabet and required padding. Base64Url uses `-`/`_` and
rejects padding. The existing codecs' strict text validation is unchanged.
Whitespace and embedded nulls are invalid.

These operations allocate nothing and are `nothrow`. Invalid text, insufficient
capacity, or encoded-length overflow returns false, zeros the result count, and
preserves destination bytes. Empty input succeeds with zero output, including
default empty spans. Output has no terminator.

Encoding requires disjoint buffers. Decoding supports disjoint buffers or the
same starting address. Keep count outputs separate from input and output
storage.

Include `Encoding/base64_span.gf` with `Encoding/Base64.gf`, or
`Encoding/base64_url_span.gf` with `Encoding/Base64Url.gf`, plus the usual span
dependencies. The pointer codecs remain usable without either span layer.
