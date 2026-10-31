# Incremental UTF-8 decoding

`Standard::Text::Utf8Decoder` carries a partial scalar between input chunks:

```cpp
using Standard::Text;

Utf8Decoder decoder = new();
char[3] euro = [(char)0xe2, (char)0x82, (char)0xac];
uint scalar = 0;
nuint consumed = 0;
Utf8Decoder::Status status =
    decoder.Read(euro, 2, false, &scalar, &consumed); // NeedMore; consumed = 2
status = decoder.Read(&euro[2], 1, true, &scalar, &consumed);
// Scalar; scalar = U+20AC; consumed = 1
status = decoder.Read(euro, 0, true, &scalar, &consumed); // End
```

## Reading chunks

`Read(input, count, final, scalar, consumed)` produces at most one scalar.

| Parameter | Meaning |
| --- | --- |
| `input`, `count` | Current chunk and its byte count |
| `final` | This is the last chunk |
| `scalar` | Scalar output, or zero for a non-scalar status |
| `consumed` | Bytes used from this call's chunk |

| Status | Next step |
| --- | --- |
| `Scalar` | Process the value, then call again on remaining bytes |
| `NeedMore` | Supply another chunk |
| `End` | Final input ended at a scalar boundary |
| `Invalid` | Malformed UTF-8 or a truncated final scalar |

Advance the chunk by `consumed` after each call. Even with `final = true`, a
`Scalar` result needs another call on the remainder, or a zero-length final call,
to establish `End`.

Empty nonfinal chunks return `NeedMore` and preserve pending state. `End` and
`Invalid` persist until `Reset()`. Reset discards pending bytes and starts a new
stream. Decoder copies have independent state.

## Errors and storage

Impossible prefixes fail immediately. For example, `e0 80` fails before a third
byte arrives. `consumed` includes the offending byte and excludes an unread
suffix. Final truncation consumes the available prefix.

Keep scalar/count outputs separate from input, each other, and decoder state.
For decoding a complete buffer with overlapping result storage, use
[Utf8](UTF8.md).

Source: `core/Text/Utf8Decoder.gf`
