# Streaming hexadecimal decoding

`Standard::Encoding::HexDecoder` implements `ITextWriter`: counted input is hexadecimal
text and its borrowed destination receives binary bytes. It reuses `Hex::TryDecode`
for complete pairs, accepting ASCII `0-9`, `a-f`, and `A-F`. Whitespace, separators,
`0x` prefixes, NUL, non-ASCII bytes, and padding are rejected.

```csharp
using Standard; using Standard::IO; using Standard::Encoding;
int main()
{
    File input = File::OpenRead("input.hex");
    File output = File::OpenWrite("output.bin");
    HexDecoder decoder = new(&output);
    char[257] buffer;
    input.CopyTo(&decoder, buffer, 257);
    decoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Both files use binary mode, preserving decoded NUL, CR/LF and high bytes. The
caller explicitly closes files to observe buffered IO failures. The decoder never
flushes, closes, completes, or destroys its destination.

`Write(data, count)` accepts arbitrary chunk boundaries and retains no input
pointer. It copies at most one encoded character between calls. For example,
writing `a`, then `F`, emits byte AF. A lone character is retained without
validation until its pair arrives: writing `g` alone may succeed, but a following
character rejects the pair, or `Complete` rejects the odd final length.

Validated pairs enter a 256-byte stack output buffer. Full blocks and each
successful call's final nonempty block reach the sink as counted bytes, without
a terminator or newline. Successful decoding/completion allocate no internal heap
storage. Destinations may allocate. Exceptions may allocate.

- An active zero-length Write reads no input, calls no sink, and preserves carry
- `Complete()` requires an even total length, emits nothing and is idempotent
  after success. Empty input is valid.
- Every Write after successful completion throws `Exception`, including zero
- Invalid pairs or odd completion throw `Exception` and permanently fail the
  decoder. Sink exceptions propagate unchanged and also fail it. Failed decoders
  reject all later Write/Complete calls without touching the sink.
- Scope destruction never calls Complete or emits a pending character. Call
  Complete explicitly to detect a truncated final byte. There is no reset.

Decoding streams rather than validating the entire input transactionally. Earlier
blocks may already be visible when later input is malformed. A valid buffered
suffix in the failing call may not have been flushed and can be discarded. The
visible prefix depends on chunk boundaries and sink behavior. There is no rollback
or fixed guaranteed prefix length. Use whole-buffer `Hex::TryDecode` when its
all-input validation and untouched-output failure contract is required.

Keep the sink alive and each input chunk stable during Write. The sink must not
overwrite or invalidate input through aliases, such as decoding from a String
into that same String. Concurrent or reentrant mutation is unsupported.

Copies duplicate carry and completion/failure flags but share the sink. Driving
them sequentially writes both branches into that destination. E.g. copies carrying
`a`, completed with `0` and `f`, write bytes A0 then AF. Flags are independent:
a copy made before another copy fails does not discover that failure. Moving
preserves state. Neither copying nor destruction transfers ownership of the sink.

For explicit source selection include `Encoding/HexDecoder.gf`, `Encoding/Hex.gf`
and the `ITextWriter`/exception contracts. [Base64Decoder](BASE64_DECODER.md) uses the same
writer/completion pattern. `WriteHex` performs hexadecimal encoding.
