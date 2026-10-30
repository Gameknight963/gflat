# Streaming Base64 decoding

`Standard::Encoding::Base64Decoder` implements `ITextWriter`: its input is encoded Base64 text,
and its borrowed destination receives decoded binary bytes. This lets existing
writer pipelines decode a stream without allocating a complete input or result
string. [Base64Encoder](BASE64_ENCODER.md) performs the opposite transformation.

## Decode a file into binary output

```csharp
using Standard; using Standard::IO; using Standard::Encoding;

int main()
{
    File input = File::OpenRead("input.txt");
    File output = File::OpenWrite("output.bin");
    Base64Decoder decoder = new(&output);
    char[4096] buffer;
    input.CopyTo(&decoder, buffer, 4096);
    decoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Both files use binary mode. In particular, the destination preserves NUL, CR, LF,
and non-ASCII decoded bytes exactly. The caller closes files explicitly to observe
buffered IO failures; the decoder never flushes or closes its destination.

## Strict input and completion

The decoder uses the existing [Base64](BINARY_CODECS.md) profile: standard `+` and
`/` alphabet, required padding for partial groups, and canonical zero unused bits.
Whitespace, line wrapping, the URL alphabet, extra padding, and trailing data
after a padded quartet are rejected. There is no BOM removal or encoding guessing.

- `Write(data, count)` accepts arbitrary counted chunks. It copies up to three
  pending encoded characters between calls, so quartet boundaries need not match
  input chunk boundaries. No input pointer is retained after the call returns.
- Complete quartets are validated before their decoded bytes are added to the
  bounded output buffer. Full output blocks and the successful call's final block
  are sent to the destination. Output contains counted binary bytes, without a
  terminator or a newline.
- A valid padded quartet marks the end of encoded input. Further nonempty input
  is rejected, whether it occurs in the same call or a subsequent call. Empty
  writes remain allowed until `Complete`.
- A zero-length write on an open decoder does not dereference its input pointer
  or call the destination.
- `Complete()` verifies that no incomplete quartet remains. It emits nothing and
  is idempotent after success. Empty input is valid and remains empty.
- After successful completion, every `Write`, including a zero-length write,
  throws `Exception`. There is no reset operation.
- Scope exit does not call `Complete`. Without explicit completion, an incomplete
  final quartet may remain undetected. Abandoning the decoder discards its carry.

For example, writing `Y`, then `WJ`, then `j` emits the three bytes `abc`. Writing
`YQ==` emits `a` and forbids any later nonempty input. Writing only `YQ` does not
emit a byte; `Complete` rejects the missing padding.

## Failure and borrowing

Invalid input or an incomplete `Complete` throws `Exception` and permanently
fails that decoder. A sink exception propagates unchanged and also fails it.
Both `Write` and `Complete` subsequently reject use without calling the sink.

This is a streaming transform, not a whole-input validation transaction. A valid
prefix may already have reached the sink before a later invalid byte is found.
Unflushed decoded bytes in the failing call may be discarded. The visible prefix
can therefore depend on chunk boundaries and sink behavior; there is no output
rollback or guaranteed prefix length. A failed file pipeline can leave a partial
decoded file.

The sink must remain alive and usable throughout operations. Keep each input
chunk alive and unchanged during `Write`. The sink must not overwrite or
invalidate it through an alias: do not decode bytes borrowed from an owning
`String` into that same `String`. Concurrent or reentrant use is unsupported,
including callbacks that use or copy the same decoder.

Copying the decoder duplicates its carry and end/completion/failure flags while
sharing the sink. Copies do not form independent output streams; do not interleave
them into the same destination. A copy made before another copy's failure cannot
discover that failure. Moving preserves the state. Destroying a decoder never
writes to or destroys its destination.

Successful decoding and completion allocate no internal heap memory. Decoded
output uses a 192-byte stack buffer, and between calls only three encoded bytes
and state are retained. The destination may allocate or throw. Invalid input and
rejected operations construct ordinary exceptions and may allocate.
