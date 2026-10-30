# Explicit buffered input

`std::IO::BufferedReader` implements `IByteReader`, owns a fixed-size `Vector<char>`
read-ahead buffer and borrows an underlying source. Its constructor accepts
`IByteReader* source, nuint capacity = 4096`. Capacity zero throws `IOException`
before allocating the buffer. Successful construction initializes every buffer byte
and requires one buffer allocation. Allocator failure propagates normally; the source
has not been read and partial construction cleans up initialized owned fields.

Use it when small reads, such as [stream varints](STREAM_VARINT.md), would otherwise
make many underlying source calls. Read-ahead is explicit: wrapping a source permits
a refill of the entire configured capacity even when the consumer requested one byte.
No global cache or prefetch thread is involved.

## Read and progress

- Pending buffered bytes are copied up to the requested count and returned immediately.
  That call does not also refill, even if it returns a short positive result.
- With no pending bytes, a positive request makes one source `Read` into the owned
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

## Ownership, moves and cursor exclusivity

The owned Vector field is noncopyable under existing value rules. Consequently the
adapter cannot be implicitly copied into a local, argument or field. Explicit move
transfers the buffer and unread cursor state; no buffer pointer points into the
adapter itself. Reinitialize a moved local before using it again. Existing pointers
to the old adapter address do not follow that move.

The concrete source must remain alive, unmoved and usable while the adapter is used.
All consuming reads, body reads and next-message reads must go through the adapter.
Do not directly read, seek, clear, move or close the underlying source while using
this buffered cursor, or install another independently consuming adapter over it.
The compiler does not prove every external alias obeys this rule.

Destruction frees only the owned buffer. It never closes, flushes or rewinds the
source. Unread prefetched bytes are abandoned when the adapter is destroyed; the
underlying cursor has already advanced past them. There is no pushback or rollback.
The output buffer passed to `Read` must not overlap adapter or source state or the
source's retained backing storage. Output pointers are borrowed for the call only.

If an underlying call throws or reports more bytes than requested, the adapter
latches failure. Further reads throw `IOException` without touching the source,
including zero-count reads. Underlying exceptions propagate unchanged and may have
consumed an unknown prefix; recovery cannot be reconstructed from counters. Pending
properties remain inspectable, but there is no reset/retry guarantee.

## One cursor for prefix and body

This complete program writes a small recorded message, then reads both its canonical
varint prefix and payload through the same buffered cursor. Opening the output
truncates an existing file at the supplied path.

```csharp
using std::IO;

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
            BufferedReader buffered = new(&source, 4096);
            IByteReader* input = &buffered;
            uint length = input.ReadVarUInt32();
            if (length != 3u) return 3;
            char[3] body;
            input.ReadExactly(body, 3);
            if (body[0] != 'a' || body[1] != 'b' || body[2] != 'c') return 4;
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
uses the selected Memory allocator; no GC, native IO implementation or compiler hook
is added by the adapter.
