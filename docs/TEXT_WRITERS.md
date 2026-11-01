# Text writer helpers

Import `Standard::Text` for line and repeated-byte operations on `ITextWriter`:

```csharp
using Standard::Text;
using Standard;

Console::Error.WriteLine("warning");
Console::Out.WriteLine("A\0B", 3);
Console::Out.WriteLine();
```

| Operation | Output |
| --- | --- |
| `WriteLine()` | One LF byte |
| `WriteLine(text)` | Bytes before the C string's first NUL, then LF |
| `WriteLine(data, count)` | Counted bytes, then LF |
| `WriteRepeated(value, count)` | One byte repeated `count` times |

Line bodies and LF are separate writes. A body failure skips LF, while an LF
failure can leave the body already written. Concrete writers apply their own
stream behavior, such as Windows Console's LF-to-CRLF translation.

```csharp
using Standard::Text;
using Standard;

int main()
{
    char[70] bytes;
    BufferWriter storage = new(bytes, 70);
    ITextWriter* writer = &storage;
    writer.WriteRepeated('-', 65);
    writer.WriteRepeated('\0', 2);
    if (storage.Length != 67 || bytes[64] != '-' || bytes[65] != '\0'
        || bytes[66] != '\0' || bytes[67] != '\0') return 1;
    return 0;
}
```

Repeated output uses chunks of at most 64 bytes. A writer error can leave a
prefix, so retrying the whole operation may duplicate it. To measure formatting,
use [CountingWriter](COUNTING_WRITER.md). To add a prefix per line, use
[LinePrefixWriter](LINE_PREFIX_WRITER.md).

## Send one stream to two writers

`TeeWriter(first, second)` forwards each write to the first destination and then
the second. This lets a formatter produce each chunk once for both outputs.

```csharp
using Standard;
using Standard::Text;

int main()
{
    char[32] bytes;
    BufferWriter output = new(bytes, 32);
    CountingWriter count = new();
    TeeWriter tee = new(&output, &count);
    ITextWriter* writer = &tee;
    writer.WriteLine("A\0B", 3);
    if (output.Length != 4 || count.Length != 4 || bytes[1] != '\0'
        || bytes[3] != '\n') return 1;
    return 0;
}
```

Keep both destinations alive. Input must remain valid through both calls and be
separate from storage either destination may modify. Passing the same endpoint
twice sends each chunk twice.

If the first destination throws, the second receives nothing from that call.
If the second throws, the first may already have accepted the complete chunk.
Later writes are forwarded normally, subject to each destination's recovery rules.

## Buffer small writes before forwarding

`BufferedWriter(destination, scratch, capacity)` collects writes in caller scratch
and sends each full block to the destination.

```csharp
using Standard;
using Standard::Text;

int main()
{
    char[32] bytes;
    BufferWriter output = new(bytes, 32);
    char[8] scratch;
    BufferedWriter buffered = new(&output, scratch, 8);
    ITextWriter* writer = &buffered;
    writer.WriteLine("A\0B", 3);
    if (output.Length != 0) return 1;
    buffered.Flush();
    if (output.Length != 4 || bytes[1] != '\0' || bytes[3] != '\n') return 2;
    return 0;
}
```

Call `Flush()` to send a partial block. It drains this buffer alone. For FileStream or
Console, flush the adapter first and the concrete stream afterward. Flush nested
buffers from the outermost inward. Scope exit discards pending bytes.

Zero capacity forwards nonempty writes directly. Empty writes and flushes make
no destination call.

Keep scratch and the destination alive and at stable addresses. Input must be
separate from scratch and storage the destination may change. A destination
exception can leave partial output and permanently fails the buffer. Later writes
and flushes throw.

Copies share scratch but duplicate pending length and status. Flushing two copies
can duplicate output, while writing through one can overwrite another's pending
bytes. Use one buffer per logical stream.

Sources in `core/Text`: `text_writer_line.gf`, `text_writer_repeated.gf`,
`TeeWriter.gf`, and `BufferedWriter.gf`.
