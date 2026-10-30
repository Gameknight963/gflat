# Streaming Base32 decoding

`Standard::Encoding::Base32Decoder` implements `ITextWriter`. It accepts counted uppercase
padded Base32 text, validates each complete group using the existing [Base32
codec](BASE32.md), and sends decoded binary bytes to one borrowed synchronous
sink. [Base32Encoder](BASE32_ENCODER.md) performs the opposite transformation.

## Decode counted input through a writer pipeline

This complete program decodes a file without materializing the complete input or
output in a String. Use distinct input and output files: opening the output truncates
it, so the output path must not name or alias the input.

```csharp
using Standard::IO;
using Standard::Encoding;

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    File input = File::OpenRead(argv[1]);
    File output = File::OpenWrite(argv[2]);
    Base32Decoder decoder = new(&output);
    IByteReader* source = &input;
    source.CopyTo(&decoder);
    decoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Binary File output preserves every decoded NUL, CR, LF and high-bit byte. Explicit
Close observes buffered File errors; the decoder never flushes or closes its sink.
The shared byte-reader helper also accepts a caller-defined reader or optional
TcpClient. An EOF-based network pipeline requires the peer to finish sending; bounded
message protocols must use their declared payload extent rather than connection EOF.

## Strict profile and completion

The profile is uppercase `A` through `Z` and digits `2` through `7`, with canonical
`=` padding on a partial group and zero unused bits. Lowercase, whitespace, line
wrapping, BOMs, alternative alphabets, invalid padding shapes and data after the final
padded group are rejected. There is no encoding guessing or normalization.

- Write accepts arbitrary chunk splits, copying at most seven pending encoded characters
  between calls. No caller input pointer is retained.
- Each complete eight-character group is validated by Base32 before adding its decoded
  bytes to a bounded batch. Full 200-byte batches and the successful call's remainder
  are sent to the sink as counted binary bytes, without terminator or newline.
- A valid padded group marks the end of encoded input. Further nonempty data throws,
  whether in that same Write or a later Write. Zero-count writes remain allowed until
  Complete; on an unfinished usable decoder they dereference no input and call no sink.
- Complete rejects any incomplete final group. It writes nothing, is idempotent after
  success, and accepts an empty stream.
- Every Write after successful completion throws, including zero count. There is no reset.
- Destruction does not call Complete or validate a pending tail. Call Complete explicitly
  even when copying reached EOF; otherwise incomplete final input might go undetected.

For example, `MY======` decodes to `f`. Writing only `MY` emits no byte and Complete
rejects the missing remainder/padding. Full eight-character groups without padding
remain valid when they represent complete five-byte input groups.

## Prefix progress and failures

Invalid input, incomplete completion or a sink exception permanently fails this decoder.
The original sink exception propagates unchanged. Subsequent Write and Complete reject
without calling the sink, including a value copy made after failure.

This is streaming validation, not the standalone codec's whole-input transaction.
A valid prefix may already have reached the destination before a later invalid group,
trailing byte or sink error. Valid decoded bytes still buffered in a failing Write
can be discarded. Visible output can depend on chunk boundaries and sink behavior;
there is no guaranteed prefix length, rollback, automatic draining or recovery. A failed
file pipeline can leave a partial decoded output file. Existing Base32::TryDecode keeps
its validate-before-write behavior when called on a complete buffer.

## Borrowing and copies

Keep the sink alive, unmoved and usable for every operation. Keep input alive and
unchanged during Write, disjoint from decoder state and any storage the sink may
mutate. Do not decode bytes borrowed from a String into that same owning String.
Concurrent or reentrant operation is unsupported, including callbacks that use or
copy the decoder during an active output call.

A normal copy duplicates the pending prefix and padding/completion/failure flags while
sharing the sink. Copies are not independent streams: do not interleave divergent
states into one destination. A copy made before another copy's failure cannot detect
that failure. Move transfers the same state. No copy or destructor owns the sink or
performs output, cleanup, close or flush on its behalf.

Normal decoding and completion allocate no internal heap storage. The output batch
is 200 stack bytes and persistent carry is seven inline characters plus flags/counter.
The chosen sink and ordinary exceptions may allocate. The decoder maintains no
whole-input buffer or total output length counter.

## Explicit sources

Include `core/Encoding/Base32Decoder.gf`, `core/Encoding/Base32.gf` and the existing
ITextWriter / Exception contracts with ordinary exception runtime support. The decoder
adds no File, libc IO, owning String, GC, new interface or compiler dependency. File
and byte-reader dependencies belong to the example's endpoints and copy helper.
