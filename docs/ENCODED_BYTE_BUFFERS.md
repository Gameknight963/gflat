# Base32 and percent-encoded byte buffers

`Base32` and `PercentEncoding` accept `readonly(byte)*` input in `TryEncode` and
`byte*` output in `TryDecode`. Their `Text` helpers accept byte input when
returning owning encoded Strings. Percent encoding also accepts byte input in
`TryGetEncodedLength`.

```csharp
using Standard::Encoding;
using Standard::Text;

int main()
{
    byte[2] input = [(byte)0, (byte)255];
    String base32 = Base32Text::Encode(&input[0], 2);
    String percent = PercentEncodingText::Encode(&input[0], 2);
    if (!base32.Equals("AD7Q====") || !percent.Equals("%00%FF")) return 1;
    byte[2] output;
    nuint written = 0;
    if (!PercentEncoding::TryDecode(percent.Data, percent.Length, &output[0], 2, ref written)) return 2;
    if (written != 2 || output[1] != (byte)255) return 3;
    return 0;
}
```

The byte overloads retain the character-buffer codecs' validation, padding,
capacity, and overlap contracts. Encoding requires disjoint buffers. Decoding
supports disjoint buffers or the exact same starting address. Invalid input or
insufficient output capacity returns false, sets `written` to zero, and leaves
the destination unchanged. Keep `written` separate from input and output.

Base32 uses canonical uppercase RFC 4648 encoding with padding. Percent encoding
operates on component bytes, not complete URIs or form data. Embedded zero and
non-ASCII bytes remain binary data and do not imply a character encoding.
Caller-buffer operations allocate nothing and are `nothrow`. Owning Text helpers
can allocate and throw.
