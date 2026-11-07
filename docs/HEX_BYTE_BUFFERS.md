# Hexadecimal byte buffers

`Hex::TryEncode` and `HexText::Encode` accept `readonly(byte)*` input directly.
`Hex::TryDecode` accepts a `byte*` destination. Counts and capacities are bytes,
with encoded output requiring two characters per input byte.

```csharp
using Standard::Encoding;
using Standard::Text;

int main()
{
    byte[3] bytes = [(byte)0, (byte)171, (byte)255];
    String text = HexText::Encode(&bytes[0], 3);
    if (!text.Equals("00ABFF")) return 1;
    byte[3] restored;
    nuint written = 0;
    if (!Hex::TryDecode(text.Data, text.Length, &restored[0], 3, ref written)) return 2;
    if (written != 3 || restored[1] != (byte)171) return 3;
    return 0;
}
```

The byte and character buffer overloads share validation and overlap rules.
Encoding requires disjoint input and output. Decoding supports disjoint buffers
or the exact same starting address for in-place shrinking. Other partial overlap
is unsupported. Invalid input or insufficient capacity returns false, sets
`written` to zero, and leaves output unchanged. Keep `written` separate from
both buffers.

The caller-buffer codecs are allocation-free and `nothrow`. `HexText::Encode`
returns an owning String and can throw on allocation failure. Output has no
terminator in the caller-buffer forms. Load `Encoding/Hex.gf` for the codecs and
`Encoding/HexText.gf` for the String convenience.
