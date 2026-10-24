# Text writer helpers

`std/core/TextWriterExtensions.gf` adds optional line and repeated-byte extensions
to borrowed `ITextWriter*` values. Bring them into scope with `using std;`:

```csharp
using std;
Console::Error.WriteLine("warning");
Console::Out.WriteLine("A\0B", 3);
Console::Out.WriteLine();
```

The parameterless overload writes one LF byte. The C-string overload scans to the
first null; the counted overload preserves every byte, including embedded nulls.
No String, GC, allocation, ownership transfer, or implicit flush is involved.
The writer itself still determines where and how bytes are stored; for example,
Windows libc text-mode standard streams translate LF to CRLF.

Counted and C-string lines write their body, then write LF separately. If the
body write throws, the LF is not attempted. If the LF write fails, the body may
already have been written. Calls do not promise atomic output or synchronization
across multiple writes. Exceptions from the underlying writer propagate.

These helpers work with any `ITextWriter*`, including borrowed File, BufferWriter,
String, and application writer implementations. They never close or destroy it;
its owner must keep it alive for the call. As with File and Console counted
writes, the caller supplies valid readable input storage.

To measure emitted bytes without storing output, use
[`CountingWriter`](COUNTING_WRITER.md). It also accepts these line helpers.

`writer.WriteRepeated(value, count)` writes one `char` byte `count` times, using
a fixed 64-byte stack buffer and counted writes of at most 64 bytes. Zero count
makes no writer calls. It supports every byte value, including NUL and high bytes;
it adds no terminator or newline. This is useful for padding or rulers, not
Unicode-character repetition.

```csharp
using std;
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

The helper allocates nothing and retains no state or pointer. The writer is
borrowed and must consume the supplied temporary span during each `Write`.
It is never flushed, closed or destroyed by the helper. A writer may allocate
or throw according to its own contract; exceptions propagate and can leave an
accepted prefix. Repeated output is not transactional, and retrying the whole
operation may duplicate that prefix. Counting uses subtraction after each
successful chunk, without multiplying or accumulating a potentially overflowing
total. No total-output buffer is allocated.

[`LinePrefixWriter`](LINE_PREFIX_WRITER.md) adds a counted prefix to each
LF-delimited input line as bytes arrive. It borrows its destination and prefix,
preserves source bytes, and needs no intermediate String or completion call.
