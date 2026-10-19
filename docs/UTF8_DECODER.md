# Incremental UTF-8 decoding

`std::Utf8Decoder` decodes strict UTF-8 across input chunks. It retains only
the partially decoded scalar, never a pointer to a previous chunk. Construction,
`Reset()`, and `Read()` are `nothrow`, allocate nothing, and require no libc,
exception runtime, String, or GC. Include `core/Utf8Decoder.gf`; the separate
whole-buffer functions in `core/Utf8.gf` remain available.

```cpp
std::Utf8Decoder decoder = new();
char[3] euro = [(char)0xe2, (char)0x82, (char)0xac];
uint scalar = 0;
nuint consumed = 0;
std::Utf8Decoder::Status status =
    decoder.Read(euro, 2, false, &scalar, &consumed); // NeedMore; consumed = 2
status = decoder.Read(&euro[2], 1, true, &scalar, &consumed);
// Scalar; scalar = U+20AC; consumed = 1
status = decoder.Read(euro, 0, true, &scalar, &consumed); // End
```

## Reading chunks

`Read(readonly(char)* input, nuint count, bool final, uint* scalar, nuint* consumed)`
returns one of the nested `Status` enum values:

| Status | Meaning |
| --- | --- |
| `Scalar` | One complete scalar was produced. U+0000 is a normal scalar. |
| `NeedMore` | All supplied bytes were consumed; more input may follow. A partial scalar is retained if necessary. |
| `End` | Final input ended at a scalar boundary. |
| `Invalid` | An invalid byte or a truncated final scalar was encountered. |

Each call produces **at most one scalar**. Advance the chunk pointer by
`consumed`, subtract that amount from its length, and call again to process
the remainder after `Scalar`. `consumed` counts only bytes from the current
call, excluding bytes saved by earlier calls. All other statuses write zero
to `scalar`.

Set `final` when the supplied chunk is the last input. A `Scalar` result does
not itself finish the decoder: process the remainder, then call with zero
bytes and `final = true` to obtain `End` or detect truncation. Empty nonfinal
chunks return `NeedMore` without changing pending state, including when the
previous call ended at a scalar boundary.

Malformed leading bytes and impossible scalar prefixes are rejected immediately.
For example, `e0 80` is invalid without waiting for a third byte. On `Invalid`,
`consumed` includes the first offending byte, but excludes any unexamined suffix.
Truncation consumes all available bytes. Both `Invalid` and `End` persist until
`Reset()`; later calls consume zero bytes. Reset explicitly discards pending
input and starts a new stream. Copying a decoder copies its independent state.

The caller supplies readable counted input and writable output variables.
No input memory is read when `count` is zero. The decoder rejects overlong
encodings, surrogate values, and values beyond U+10FFFF; it performs no
replacement, normalization, or grapheme processing.
