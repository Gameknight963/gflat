# Streaming Base64 output

`std::Base64Writer` implements `ITextWriter` and encodes counted binary input
to one borrowed destination. It emits standard padded Base64 with the `+` and
`/` alphabet, without whitespace, line wrapping, a BOM, or a trailing newline.
It reuses the existing [Base64 codec](BINARY_CODECS.md).

## Copy a file through an encoder

This complete program writes `input.bin` as Base64 in `output.txt`:

```csharp
using std;

int main()
{
    File input = File::OpenRead("input.bin");
    File output = File::OpenWrite("output.txt");
    Base64Writer encoder = new(&output);
    char[4096] buffer;
    input.CopyTo(&encoder, buffer, 4096);
    encoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Neither the full input nor the full encoded text is held in a `String`.
`File::OpenWrite` uses binary mode, so the file receives the exact encoded bytes.
The caller closes the files explicitly to observe buffered IO errors. The
encoder never flushes or closes its destination.

## Chunk boundaries and completion

- `Write(data, count)` accepts arbitrary counted byte chunks, including NUL and
  non-ASCII values. Chunk boundaries do not affect the final encoding.
- Whole three-byte groups are encoded immediately in bounded blocks. At most two
  input bytes are copied into the writer's state between calls. The input pointer
  is never retained, so the caller may reuse its buffer after `Write` returns.
- A zero-length write on an open writer does not dereference its input pointer
  or call the destination.
- `Complete()` emits a final four-character padded group if one or two bytes
  remain. With no pending bytes it emits nothing. It does not write a newline.
  Repeated successful calls do nothing.
- After successful completion, every `Write`, including a zero-length write,
  throws `Exception`. There is no reset or restart operation.
- Scope exit never emits padding or calls `Complete`. The caller must complete
  the stream explicitly; abandoning an incomplete writer discards its carry.

For example, writing `a` and then `bc` yields `YWJj`, with no padding. Writing only
`a` yields no output until `Complete()` emits `YQ==`. An empty stream remains empty.

## Borrowing, copies, and failures

Keep the destination alive and usable for every operation. Input must remain alive
and unchanged during each `Write`. The destination must not overwrite or
invalidate the input while the writer is reading it; do not encode bytes borrowed
from a `String` into that same owning `String`. Concurrent or reentrant use of the
writer is unsupported, including calling it again from the destination callback.

Copying the writer duplicates its carry and completion/failure flags while sharing
the destination pointer. Copies are not independent output streams: do not
interleave their operations into the same stream. A failure is local to that
writer; a copy made beforehand cannot discover it. Moving transfers the same
state. Destroying any copy never destroys its destination.

A sink exception propagates unchanged, possibly after partial output. The writer
then rejects both `Write` and `Complete` with `Exception`, without calling the sink
again. There is no rollback or recovery through this writer. A new writer should
be used only after the caller has dealt with the incomplete output.

Normal encoding and completion perform no internal heap allocation. Output uses a
256-byte stack buffer, and carry uses two copied bytes. The destination may
allocate or throw; rejected operations construct ordinary exceptions and may also
allocate. The encoder does not impose a total input-size counter or a complete
output-size calculation.
