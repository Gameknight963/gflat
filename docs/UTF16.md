# Strict UTF-16 code-unit helpers

`Standard::Text::Utf16` encodes and decodes Unicode scalars in `ushort` code
units. Lengths, capacities, and consumed/written counts measure units. To read
serialized UTF-16LE or UTF-16BE bytes, decode their byte order first.

| Function | Result |
| --- | --- |
| `IsValidScalar(scalar)` | True for U+0000–U+10FFFF, excluding surrogates |
| `EncodedLength(scalar)` | 1–2 units, or zero for an invalid scalar |
| `TryDecode(input, count, scalar, consumed)` | First scalar and its unit count |
| `TryDecodeLast(input, count, scalar, consumed)` | Final scalar and its unit count |
| `TryEncode(scalar, output, capacity, written)` | Encoded scalar |
| `TryCount(input, count, scalarCount)` | Validates input and counts scalars |
| `IsValid(input, count)` | Validates input |

High surrogates U+D800–U+DBFF require a following low surrogate U+DC00–U+DFFF.
Malformed or incomplete pairs fail. U+0000, U+FEFF, and Unicode noncharacters
are ordinary scalar values.

Failed Try calls zero their outputs. Failed encoding preserves the buffer.
Decode/count outputs may overlap aligned writable input, but must be separate
from each other. Result stores occur after reading and can overwrite input.
Keep an encoder's written-count output separate from its destination.

`TryDecodeLast` validates the final scalar alone. Its consumed count can shorten
a view by one scalar:

```csharp
int main()
{
    ushort[3] text = [(ushort)65, (ushort)0xd83d, (ushort)0xde00];
    nuint length = 3;
    uint scalar = 0u;
    nuint consumed = 0;
    if (!Standard::Text::Utf16::TryDecodeLast(text, length, &scalar, &consumed)) return 1;
    if (scalar != 0x1f600u || consumed != (nuint)2) return 2;
    length -= consumed;
    if (length != (nuint)1 || text[0] != (ushort)65) return 3;
    return 0;
}
```

## Incremental decoding

`Utf16Decoder.Read(input, count, final, scalar, consumed)` carries a high surrogate
between chunks and produces at most one scalar per call. `consumed` counts units
from the current chunk.

| Status | Next step |
| --- | --- |
| `Scalar` | Process the value, then call again on remaining units |
| `NeedMore` | Supply another chunk |
| `End` | Final input ended at a scalar boundary |
| `Invalid` | Malformed pair or truncated final high surrogate |

A final `Scalar` result still needs another call on the remainder, or a
zero-length final call, to establish `End`. Empty nonfinal calls preserve partial
state. `End` and `Invalid` persist until `Reset()`.

Keep incremental outputs separate from input, each other, and decoder state.

Sources: `core/Text/utf16.gf`, `core/Text/Utf16Decoder.gf`
