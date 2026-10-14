# libc-dependent facilities

`ExceptionHandling.gf` supplies the automatically included weak
`Exceptions::Unhandled(Exception*? error)` hook. Its default `void` implementation calls libc `exit` with the exception's
`GetCode()` (1 by default), or 1 for a foreign exception, without printing.
Replace it to report the error and terminate explicitly. If it returns or throws,
the runtime exits with status 1.

`Formatting.gf` supplies `std::Format(float, ITextWriter*)` and
`std::Format(double, ITextWriter*)` through `snprintf`, using 9 and 17 significant
digits respectively. Formatting follows the application's current C locale.
The result is written from a stack buffer; there is no intermediate heap string.

Supply only core sources when these facilities are unwanted. `std.gfproj`
currently includes both directories.

`Memory.gf` supplies the automatically included weak `Memory::Allocate` and
`Memory::Free` implementations using libc. Replace both functions to provide a
custom allocator. This default is kept in libc even though the core library calls
the abstracted memory hooks; libc remains an unconditional dependency for now.

## Console

`Console.gf` provides C-string and caller-buffer APIs. It needs `core/IOException.gf`
and `core/BufferTooSmallException.gf`, but does not require `String.gf`.
`ConsoleString.gf` adds the optional owning-string conveniences. Referencing
`std/std.gfproj` includes both.

```gflat
int main()
{
    Console::Write("Name: ");
    Console::Flush();
    char[128] buffer;
    nuint length = Console::ReadLine(buffer, 128);
    Console::Write("Hello, ");
    Console::WriteLine(buffer, length);
    return 0;
}
```

- `Write(text)` writes a null-terminated C string without a newline.
- `Write(data, length)` writes counted bytes, including embedded nulls.
- `WriteLine()` writes a newline. Its C-string and counted overloads write text first.
- `Flush()` flushes stdout and throws `std::IOException` on failure.
- `TryReadLine(data, capacity, ref length)` uses caller-owned storage without
  calling `Memory::Allocate` on the normal path. Capacity includes room for the null terminator;
  zero capacity is rejected before reading. The caller provides valid writable storage.
- `ReadLine(data, capacity)` returns the byte count and throws
  `std::EndOfStreamException` when no line remains. It also leaves a trailing null.
- EOF before any bytes returns `false` from `TryReadLine`, with an empty buffer and
  zero length. Empty lines return `true`. A final line without a newline is returned.
- LF and CRLF endings are removed. Other carriage returns remain text.
- If a line is too large, the whole line is consumed, a null-terminated prefix and
  its length remain in the buffer, and `std::BufferTooSmallException` is thrown.
  The next read starts at the next line. Actual read/write failures throw
  `std::IOException`; partial input/output can already have occurred.

With the String conveniences:

```gflat
using std;
int main()
{
    String line = new String();
    while (Console::TryReadLine(ref line)) Console::WriteLine(ref line);
    return 0;
}
```

`ReadLine()` returns an owning `std::String` and throws at EOF. `TryReadLine(ref line)`
clears and reuses the existing string's capacity. The `Write(ref readonly(String))`
and `WriteLine(ref readonly(String))` overloads borrow its counted buffer; they do
not copy the string or allocate. On read errors the reused String can contain a
partial line.

These APIs operate on bytes through libc's standard text streams, including its
platform newline translation. They do not select the Windows console code page.
The compiler links the small native standard-stream accessor only when referenced;
`stdin` and `stdout` are macros rather than portable external variable names.
`gflat --run` inherits console input/output, so flushed prompts appear immediately.

## Files

`File.gf` provides a move-only owning `std::File` for binary byte IO. It needs
`core/IOException.gf`; it does not depend on `String` or allocate a gflat buffer.
The nullable `std::FileHandle` alias represents libc's opaque `FILE*` internally.
It is not a Windows kernel handle, and callers must not free it through `Memory`.

```gflat
using std;
int main()
{
    File output = File::OpenWrite("message.txt");
    output.Write("Hello\n");
    output.Close();
    return 0;
}
```

- `OpenRead`, `OpenWrite`, and `OpenAppend` use binary libc modes. `OpenWrite`
  truncates an existing file; append creates a file when needed.
- `Read(data, capacity)` returns the bytes read. Zero at EOF is normal; IO
  failures throw `IOException`. Reads do not add a null terminator.
- `Write(data, count)` preserves embedded zero bytes; `Write(text)` stops at
  the first zero. Partial IO may already have occurred when an error is thrown.
- `Flush` and `Close` report failures. `Close` is idempotent and leaves the
  object closed even if libc reports a close failure.
- Scope destruction closes without throwing. Call `Close` explicitly when
  successful final flushing matters. `IsOpen` reports whether it owns a stream.
- A default `File` is closed. Move transfers ownership without a copy; ordinary
  copying is rejected. The source follows the language's moved-value rules.
- Paths use libc's narrow-character encoding. This first version does not provide
  full Unicode filename support on Windows, seeking, async IO, or directory APIs.

`examples/search` is a small command-line line-search tool using this API.
