# UTF-8 codec

`Standard::Text::Utf8` encodes and decodes Unicode scalars. A scalar occupies one
through four UTF-8 bytes. A displayed grapheme can contain several scalars.

| Function | Result |
| --- | --- |
| `IsValidScalar(scalar)` | True for U+0000–U+10FFFF, excluding surrogates U+D800–U+DFFF |
| `EncodedLength(scalar)` | 1–4 bytes, or zero for an invalid scalar |
| `TryDecode(input, count, &scalar, &consumed)` | First scalar and its byte length |
| `TryDecodeLast(input, count, &scalar, &consumed)` | Final scalar and its byte length |
| `TryEncode(scalar, output, capacity, &written)` | Encoded scalar |
| `TryCount(input, count, &scalarCount)` | Validates all input and counts scalars |
| `IsValid(input, count)` | Validates all input |

`count`, `capacity`, `consumed`, and `written` measure bytes. Decoding uses a
`uint` scalar output. Encoded output is counted text without a terminator.

Malformed or incomplete UTF-8 fails, including overlong sequences, encoded
surrogates, and values above U+10FFFF. Failed Try calls zero their outputs.
Encoding failure preserves the output buffer. Empty input validates and counts
as zero scalars, while decoding one scalar fails.

`TryDecode` and `TryDecodeLast` validate the selected scalar alone. Use `IsValid`
or `TryCount` to validate an entire buffer. Repeated final-scalar decoding can
walk a counted string backward:

```csharp
int main()
{
    char[5] text = ['A', (char)0xf0, (char)0x9f, (char)0x98, (char)0x80];
    nuint length = 5;
    uint scalar = 0u;
    nuint consumed = 0;
    if (!Standard::Text::Utf8::TryDecodeLast(text, length, &scalar, &consumed)) return 1;
    if (scalar != 0x1f600u || consumed != (nuint)4) return 2;
    length -= consumed;
    if (length != (nuint)1 || text[0] != 'A') return 3;
    return 0;
}
```

## Overlapping results

Decode outputs may overlap aligned writable input, but must be separate from
each other. Reading finishes before result stores. Those stores can overwrite
the remaining input, including when a failed decode writes zero. Keep metadata
separate if the rest of the input must survive.

For encoding, keep `written` separate from the output buffer.

Source: `core/Text/utf8.gf`. For chunked input, use [Utf8Decoder](UTF8_DECODER.md).
