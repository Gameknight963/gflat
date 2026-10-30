# Explicit buffered input

`Standard::IO::BufferedReader` implements `IByteReader` and borrows an underlying
source cursor. Choose its buffer when constructing it:

- `BufferedReader(input, capacity = 4096)` owns a fixed-size `Vector<char>` buffer.
  Successful construction initializes every buffer byte and makes one buffer
  allocation. Allocator failure leaves the source unread and cleans up initialized
  owned fields.
- `BufferedReader(input, storage, capacity)` borrows a caller-provided writable
  region of at least capacity bytes. It allocates no buffer and does not initialize
  the region. Keep those bytes alive, unmoved, and reserved for this adapter until
  it is destroyed. They must not overlap the reader, source state, retained input,
  or output buffers passed to Read.

Both overloads reject zero capacity with `IOException` before reading source input
or accessing scratch. Throwing that error uses normal exception runtime storage;
it is separate from buffer allocation. The existing constructor and default
capacity retain their behavior.


Use it when small reads, such as [stream varints](STREAM_VARINT.md) or
[counted lines](BYTE_READERS.md#read-counted-lines-from-any-byte-source), would
otherwise make many underlying source calls. Read-ahead is explicit: wrapping a source permits
a refill of the entire configured capacity even when the consumer requested one byte.
No global cache or prefetch thread is involved.

## Read and progress

- Pending buffered bytes are copied up to the requested count and returned immediately.
  That call does not also refill, even if it returns a short positive result.
- With no pending bytes, a positive request makes one source `Read` into its
  buffer, requesting its full capacity. It then returns at most the requested count.
  It performs no direct large-read bypass or second refill in the same call.
- A positive read returning zero means that refill observed EOF. EOF is not cached:
  a later read can consult the source again.
- A zero-count request calls the underlying `Read(data, 0)` once to preserve source
  state validation. It consumes no pending bytes and the adapter never dereferences
  that output pointer. Closed or otherwise unusable sources may still throw.
- `BufferedCount` reports the unread prefetched bytes. `Capacity` reports the fixed
  buffer size. Neither property reads the source. No underlying source pointer,
  seek, discard, reset or rebind API is exposed.

The source controls how long its `Read` blocks. In particular, `File::Read` uses
`fread`: refilling a large buffer from a pipe or interactive file-like source can
wait for the requested bytes or EOF. Wrapping such a source can change latency even
for a one-byte consumer. File already has libc buffering; this adapter is not an
unconditional performance improvement for File. TCP `recv` can return a short
available prefix, which the adapter accepts normally.

## Peek without consuming

`PeekByte()` returns the next byte as an int from 0 through 255, or -1 when a refill
reports EOF. Byte 255 is data. Pending bytes are returned without another source
read; repeated peeks return the same byte and leave BufferedCount unchanged.
The next ReadByte, ReadExactly, or Read through this same adapter starts with that
peeked byte.

An empty buffer can refill to its configured capacity, so peeking may block and
advance the underlying source's physical cursor. It does not advance this adapter's
logical cursor or allocate another buffer. Continue through the same BufferedReader
instead of bypassing its retained suffix. EOF is not cached permanently.

PeekByte observes the existing failure latch before returning cached input. An error
or malformed count recorded by Read(data, 0) therefore also blocks later peeks.
The operation is on BufferedReader rather than IByteReader: the retained buffer
provides lookahead without requiring provider pushback.

## Ownership, moves and cursor exclusivity

The Vector owner field remains noncopyable under existing value rules, including
when that field is empty in borrowed-buffer mode. Consequently the
adapter cannot be implicitly copied into a local, argument or field. Explicit move
transfers the buffer and unread cursor state; no buffer pointer points into the
adapter itself. Reinitialize a moved local before using it again. Existing pointers
to the old adapter address do not follow that move.

The concrete source must remain alive, unmoved and usable while the adapter is used.
All consuming reads, body reads and next-message reads must go through the adapter.
Do not directly read, seek, clear, move or close the underlying source while using
this buffered cursor, or install another independently consuming adapter over it.
The compiler does not prove every external alias obeys this rule.

Destruction frees an owned buffer only; caller scratch is never freed. It never closes, flushes or rewinds the
source. Unread prefetched bytes are abandoned when the adapter is destroyed; the
underlying cursor has already advanced past them. There is no pushback or rollback.
Do not reuse or modify caller scratch while the adapter may hold unread bytes.
Moving inline scratch, reallocating its buffer, or freeing it invalidates the borrow.
A heap allocation can keep its address through an ownership move, but its new owner
must still outlive the adapter's use.

The output buffer passed to `Read` must not overlap scratch, adapter/source state or the
source's retained backing storage. Output pointers are borrowed for the call only.

If an underlying call throws or reports more bytes than requested, the adapter
latches failure. Further reads and peeks throw `IOException` without touching the source,
including zero-count reads. Underlying exceptions propagate unchanged and may have
consumed an unknown prefix; recovery cannot be reconstructed from counters. Pending
properties remain inspectable, but there is no reset/retry guarantee.

## One cursor for prefix and body

This complete program writes a small recorded message, then reads both its canonical
varint prefix and payload through the same buffered cursor. Opening the output
truncates an existing file at the supplied path.

```csharp
using Standard::IO;

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    {
        File output = File::OpenWrite(argv[1]);
        WriteVarUInt32(&output, 3u);
        output.Write("abc", 3);
        output.Close();
    }
    {
        File source = File::OpenRead(argv[1]);
        {
            char[4096] scratch;
            BufferedReader buffered = new(&source, scratch, 4096);
            if (buffered.PeekByte() != 3 || buffered.PeekByte() != 3) return 5;
            IByteReader* input = &buffered;
            uint length = input.ReadVarUInt32();
            if (length != 3u) return 3;
            char[3] body;
            input.ReadExactly(body, 3);
            if (body[0] != 'a' || body[1] != 'b' || body[2] != 'c') return 4;
            if (buffered.PeekByte() != -1) return 6;
        }
        source.Close();
    }
    return 0;
}
```

The same prefix/body code accepts an optional open `Windows::TcpClient` as the
constructor's source. Continue reading any next message through `input` as well:
bytes of that message might already be prefetched. Production code must enforce its
own payload-size limits before allocating or reading a declared body.

## Dependencies

`core/IO/BufferedReader.gf` uses existing `IByteReader`, `IOException`, Vector and Span
APIs. Manual source selections must include the normal Vector/Span/query-interface,
Memory/BadAllocException and Exception dependencies. No buffer elements are accessed
outside initialized Vector count; reserve alone is not treated as initialized storage.
The normal standard-library source glob includes these dependencies. Construction
uses the selected Memory allocator in owned mode; borrowed mode retains the same
source dependencies but makes no buffer allocation. No GC, native IO implementation or compiler hook
is added by the adapter.
