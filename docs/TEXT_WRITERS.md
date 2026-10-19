# Text writer line helpers

`std/core/TextWriterExtensions.gf` adds three optional extensions to borrowed
`ITextWriter*` values. Bring them into scope with `using std;`:

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
