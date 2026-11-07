# Percent-encode span data

Optional span extensions provide `TryEncodePercent`, `TryDecodePercent`,
`TryGetPercentEncodedLength`, and `TryGetPercentDecodedLength`. Free functions
with the same names are available in `Standard::Encoding`.

```csharp
using Standard::Encoding;
using Standard::Memory;

int main()
{
    readonly(char)* input = "a b";
    char[5] output;
    nuint written = 0;
    if (!new Span<readonly(char)>(input, 3).TryEncodePercent(new Span<char>(&output[0], 5), ref written)) return 1;
    if (written != 5 || output[1] != '%' || output[2] != '2' || output[3] != '0') return 2;
    return 0;
}
```

Encoding accepts byte or char input and writes a char span. Decoding accepts a
readonly char span and writes bytes or chars. This encodes component bytes,
not an entire URI. UTF-8 text works by encoding its bytes. Arbitrary binary input
is also supported without Unicode validation. Spaces become `%20`, and decoding
leaves `+` unchanged. Decoding processes escapes once and accepts either hex case.

All operations allocate nothing and are `nothrow`. Malformed escapes,
insufficient capacity, or encoded-length overflow returns false, zeros the result
count, and preserves destination bytes. Empty input succeeds with zero output,
including default empty spans. Counted input can contain null bytes. No output
terminator is added.

Encoding requires disjoint buffers. Decoding supports disjoint buffers or the
same starting address. Keep scalar count outputs separate from both buffers.

Include `Encoding/percent_encoding_span.gf` with `Encoding/PercentEncoding.gf`
and the usual span dependencies. The core pointer codec remains usable without
the span layer.
