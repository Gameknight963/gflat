# Convert hex through spans

The optional `TryEncodeHex`, `TryDecodeHex`, and `TryGetHexDecodedLength`
extensions accept counted span views. Free functions with the same names are
available in `Standard::Encoding`.

```csharp
using Standard::Encoding;
using Standard::Memory;

int main()
{
    byte[3] input = [(byte)0, (byte)127, (byte)255];
    char[6] text;
    nuint written = 0;
    if (!new Span<readonly(byte)>(&input[0], 3).TryEncodeHex(new Span<char>(&text[0], 6), ref written)) return 1;
    byte[3] decoded;
    if (!new Span<readonly(char)>(&text[0], written).TryDecodeHex(new Span<byte>(&decoded[0], 3), ref written)) return 2;
    if (written != 3 || decoded[2] != (byte)255) return 3;
    return 0;
}
```

Encoding accepts byte or char spans and writes uppercase hex by default.
Pass `false` as the final argument for lowercase. Decoding accepts either case
and writes a byte or char span. Text must contain complete hex pairs without
whitespace, prefixes, separators, or null terminators.

All operations allocate nothing and are `nothrow`. Invalid text, insufficient
capacity, or encoded-size overflow returns false, zeros the result count, and
preserves destination bytes. Empty input succeeds with zero output, including
default empty spans.

Encoding requires disjoint buffers. Decoding supports disjoint buffers or the
exact same starting address. Keep count outputs separate from input and output
storage. No terminator is written.

Include `Encoding/hex_span.gf` alongside `Encoding/Hex.gf` and the usual span
dependencies. The core pointer codec remains usable without the span layer.
