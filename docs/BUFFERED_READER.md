# Explicit buffered input

`Standard::IO::BufferedReader` groups small reads from an `IByteReader`.

| Constructor | Buffer |
| --- | --- |
| `BufferedReader(input, capacity = 4096)` | Owns a fixed-size buffer |
| `BufferedReader(input, storage, capacity)` | Borrows caller scratch |

Zero capacity throws `IOException`. Keep the source and any borrowed scratch
alive and at stable addresses.

## Read and progress

- Pending bytes are returned immediately, even when fewer than requested
- With an empty buffer, one source read requests the full buffer capacity
- `BufferedCount` reports unread prefetched bytes
- `Capacity` reports the fixed buffer size
- A zero-count request forwards `Read(data, 0)` for source-state validation

A refill can read ahead even when the consumer requested one byte. Continue all
later consumption through the same adapter to retain that prefetched suffix.

Refill latency depends on the source. FileStream's `fread` can wait for the requested
bytes or EOF on a pipe, while TCP `recv` may return an available short prefix.
Choose capacity with that behavior in mind.

## Peek without consuming

`PeekByte()` returns 0–255 or -1 at EOF. Repeated peeks return the same pending
byte. With an empty buffer, peeking refills and can advance the underlying cursor.
The next read through this adapter still receives the peeked byte.

EOF is checked again on later calls, so an underlying source that gains more data
can resume.

## Ownership, moves and cursor exclusivity

The adapter is move-only in both buffer modes. Moving transfers unread cursor
state and owned storage. Pointers to the old adapter address become stale.

Destruction releases the owned buffer and abandons unread prefetched bytes.
The underlying source has already advanced past those bytes. Keep the adapter
until all needed input has been consumed.

Read output must be separate from scratch and adapter/source state. While the
adapter holds pending bytes, reserve borrowed scratch exclusively for it.

A source exception or over-reported count permanently fails the adapter. Later
reads and peeks throw `IOException`, including zero-count reads. Counters remain
inspectable, but an exception can leave an unknown amount of input consumed.

## One cursor for prefix and body

This example records a length-prefixed payload, then reads it through one cursor.
Opening the output truncates the supplied file.

```csharp
using Standard::IO;

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    {
        FileStream output = File::OpenWrite(argv[1]);
        WriteVarUInt32(&output, 3u);
        output.Write("abc", 3);
        output.Close();
    }
    {
        FileStream source = File::OpenRead(argv[1]);
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

The same code accepts a Windows TcpClient. Enforce application size limits before
allocating or reading a declared payload, then read the next message through the
same buffered cursor.

## Dependencies

Source: `core/IO/BufferedReader.gf`, using IByteReader, Vector, and IO exceptions.
