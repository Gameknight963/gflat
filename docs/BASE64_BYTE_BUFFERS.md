# Base64 byte buffers

`Base64::TryEncode` and `Base64Url::TryEncode` accept `readonly(byte)*` input.
Their `TryDecode` methods accept `byte*` output. `Base64Text::Encode` and
`Base64UrlText::Encode` also accept byte pointers and return owning Strings.

```csharp
using Standard::Encoding;
using Standard::Text;

int main()
{
    byte[2] bytes = [(byte)251, (byte)255];
    String standard = Base64Text::Encode(&bytes[0], 2);
    String url = Base64UrlText::Encode(&bytes[0], 2);
    if (!standard.Equals("+/8=") || !url.Equals("-_8")) return 1;
    byte[2] restored;
    nuint written = 0;
    if (!Base64Url::TryDecode(url.Data, url.Length, &restored[0], 2, ref written)) return 2;
    if (written != 2 || restored[0] != (byte)251 || restored[1] != (byte)255) return 3;
    return 0;
}
```

Standard Base64 uses its existing padded alphabet. Base64Url uses the URL-safe
alphabet without padding. Both retain strict validation, including canonical
trailing bits. Caller-buffer codecs are allocation-free and `nothrow`.
Invalid input or insufficient capacity returns false, writes zero to `written`,
and leaves the destination unchanged.

Encoding requires disjoint input and output. Decoding supports disjoint storage
or the exact same starting address for in-place shrinking. Keep `written`
separate from input and output. Text helpers can allocate and throw.

Load the codec's file under `Encoding` in Standard's core source root, plus its
matching `Text` file when an owning String result is needed.
