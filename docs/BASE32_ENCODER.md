# Streaming Base32 output

`std::Encoding::Base32Encoder` implements `ITextWriter`, encoding counted bytes to
one borrowed synchronous sink. It reuses the existing uppercase padded
[Base32 codec](BASE32.md). It adds no whitespace, line wrapping, BOM or newline.

## Copy counted input through an encoder

This complete program writes a binary file as Base32 without materializing the full
input or output in a String. Use distinct input and output files: opening the output
truncates an existing file, so it must not name or alias the input.

```csharp
using std::IO;
using std::Encoding;

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    File input = File::OpenRead(argv[1]);
    File output = File::OpenWrite(argv[2]);
    Base32Encoder encoder = new(&output);
    IByteReader* source = &input;
    source.CopyTo(&encoder);
    encoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

The shared [byte-reader helper](BYTE_READERS.md) also accepts a TcpClient or a
caller-defined reader. EOF-based copying over TCP requires the peer to finish sending;
message-framed protocols should read the bounded payload through their own parser.
File's binary output preserves the exact encoded bytes. The caller closes files
explicitly to observe buffered IO errors. The encoder never flushes or closes its sink.

## Chunks and final padding

- `Write(data, count)` accepts arbitrary chunks of counted binary bytes, including NUL
  and non-ASCII values. Chunk boundaries do not change the final representation.
- Complete five-byte groups are encoded immediately, in blocks of at most 160 input
  bytes and 256 output bytes. Between calls the value retains at most four copied input
  bytes. It never retains the caller's pointer, so input can be reused after return.
- A zero-length write on a usable unfinished encoder touches no input and makes no
  sink call. Its pointer still has the ordinary non-null language type.
- `Complete()` emits the final eight-character padded group when one through four
  bytes remain. With no pending bytes it writes nothing. Repeated successful completion
  does nothing. It does not append a newline.
- Every `Write` after completion, including zero count, throws `Exception`.
- Scope exit does not call `Complete`; abandoning the value discards any pending tail.

Only `Complete` can emit padding: full five-byte groups produce eight characters
without padding. For example, writing only `f` emits nothing until completion produces
`MY======`. Empty input remains empty, including on completion.

## Borrowing, copies and errors

Keep the sink alive, unmoved and usable throughout encoder use. Input must remain
alive and unchanged during each Write, and must be disjoint from encoder state and
storage the sink may mutate. Do not encode a String's borrowed bytes into that same
owning String. Reentrant or concurrent encoder use is unsupported, including callbacks
that attempt to operate on the encoder during an output write.

An ordinary value copy duplicates the pending prefix and completion/failure flags,
while sharing the sink pointer. Such copies are not independent output streams:
do not interleave operations from their divergent prefixes into one destination. A
failure affects that value only; an earlier copy cannot discover it. Move transfers
the same prefix/state. No copy or destruction owns or closes the sink.

A sink exception propagates unchanged and can follow partial output. The encoder then
rejects both Write and Complete with Exception without calling the sink again. There is
no reset or rollback. Begin a new encoder only after handling any incomplete output.
Errors while encoding a bounded block likewise latch failure.

Normal Write and Complete use fixed stack/inline storage with no internal heap
allocation, GC or total-input-sized buffer. The encoded block buffer is 256 bytes and
persistent carry is four bytes plus counters/flags. The destination and ordinary
exceptions may allocate. No global total-output length is computed or retained.

## Explicit source selection

Include `core/Encoding/Base32Encoder.gf`, `core/Encoding/Base32.gf` and the existing
ITextWriter / Exception contracts with ordinary exception runtime support. The encoder
adds no File, libc IO, owning String, GC or compiler-specific source requirement.
The example additionally uses File and the shared byte-reader helpers normally supplied
by the standard-library project. The existing Base32 codec remains independently usable.
