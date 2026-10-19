# libc-dependent facilities

`Exceptions/ExceptionHandling.gf` supplies the automatically included weak
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

`Memory/Memory.gf` supplies the automatically included weak `Memory::Allocate` and
`Memory::Free` implementations using libc. Replace both functions to provide a
custom allocator. This default is kept in libc even though the core library calls
the abstracted memory hooks; libc remains an unconditional dependency for now.

## Console

`Console/Console.gf` provides C-string and caller-buffer APIs. It needs `core/IOException.gf`,
`core/EndOfStreamException.gf`, and `core/BufferTooSmallException.gf`, but does not require `String.gf`.
`Console/ConsoleString.gf` adds the optional owning-string conveniences. Referencing
`std/std.gfproj` includes both.

`Console/Writer.gf` adds `Console::Writer`, an `ITextWriter` adapter borrowing
stdout. Pass its address to formatting functions to stream bytes without an
intermediate string. It does not own or close stdout; call `Console::Flush()`
explicitly when required:

```csharp
using std;
int main()
{
    Console::Writer output = new Console::Writer();
    int value = 42;
    value.Format(&output);
    Console::WriteLine();
    return 0;
}
```

```csharp
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

```csharp
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

```csharp
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
- `ReadExactly(data, count)` fills the requested region or throws `IOException`.
  On a truncated file the available prefix has already been consumed and stored.
- `ReadByte()` returns an integer from 0 through 255, or -1 at EOF; read errors
  throw. `WriteByte(char)` writes exactly one byte, including zero.
- `CopyTo(destination, buffer, capacity)` borrows a `File*` and caller-owned
  buffer, returning the copied byte count. It starts at the current position,
  leaves both streams open, and rejects zero capacity and copying to itself.
  `CopyTo(destination)` uses a 4096-byte stack buffer. Callers must not modify
  either stream concurrently. Copying is not transactional: failures can leave a
  partial destination, and the source may have advanced.
- `Write(data, count)` preserves embedded zero bytes; `Write(text)` stops at
  the first zero. Partial IO may already have occurred when an error is thrown.
- `WriteLine()` writes one LF byte on every platform. C-string and counted
  overloads write their contents followed by LF, with no implicit flush.
- `File::Remove(path)` and `File::Rename(source, destination)` throw `IOException`
  on failure. Rename follows the host C runtime's rules, including whether an
  existing destination can be replaced; it does not promise cross-volume moves.
- `Flush` and `Close` report failures. `Close` is idempotent and leaves the
  object closed even if libc reports a close failure.
- Scope destruction closes without throwing. Call `Close` explicitly when
  successful final flushing matters. `IsOpen` reports whether it owns a stream.
- A default `File` is closed. Move transfers ownership without a copy; ordinary
  copying is rejected. The source follows the language's moved-value rules.
- Paths use libc's narrow-character encoding. This first version does not provide
  full Unicode filename support on Windows, seeking, async IO, or directory APIs.

`examples/search` is a small command-line line-search tool using this API.

### Owning text conveniences

`File/FileText.gf` adds `std::FileText`, requiring the core `String` facilities.
`FileText::ReadAll(path)` returns an owning `String`; embedded zeros are retained.
It reads raw bytes without validating UTF-8, removing a BOM, or translating
newlines. Memory grows with the file; use `File::Read` or `CopyTo` for bounded
storage. `WriteAll` truncates and `AppendAll` appends; both accept a C string,
counted bytes, or `ref readonly(String)`. These helpers explicitly close the
file so final flush/close failures are reported. They are not atomic updates.

## Managed memory

`GC.gf` defines `std::TracingCollector`, a single-threaded, non-moving collector
written in gflat. Include that source or reference `std/std.gfproj`. The global
binding is a constrained, replaceable alias:

```csharp
public weak alias GC : std::IGarbageCollector = std::TracingCollector;
```

Applications can select another provider with `replace alias GC = MyCollector;`.
The interface lives in core and requires static `Allocate`, `ReferenceAdded`,
`ReferenceRemoved`, and `Collect` operations. The compiler calls the first three
for managed allocation and reference lifetimes; collection policy is library code.
`T^` is shorthand for `managed<T, GC>`. Explicit references such as
`managed<Node, OtherCollector>` retain their collector identity across calls.
`new^ Node() with OtherCollector` selects that collector directly.

`GC::Collect()` returns the number of allocations reclaimed. The concrete
provider also exposes `std::TracingCollector::ManagedCount` and `ManagedBytes`;
these are intentionally outside the common contract and unavailable through the
constrained alias. `CollectionThreshold()` is an ordinary weak static method,
defaulting to 65536 bytes allocated since the last collection. A qualified
replacement selects another threshold; zero disables automatic collection.
Only allocation consults this budget. Adoption does not collect, and there are
no compiler-inserted polls or collection requests at unrelated calls.

`Allocate(nuint bytes, nuint alignment)` obtains native storage through the chosen
`Memory::Allocate`, registers it, and zeroes its bytes before construction. Zero
bytes request one byte. On allocation failure it collects and retries once before
throwing `std::BadAllocException`. Unsatisfied alignment is rejected and freed;
registration failure also frees the pending native allocation. Constructor
failure leaves the allocation collectible after its temporary root is retired.

`std::TracingCollector::Adopt<T>(T* storage)` accepts an already constructed,
destructor-free object and returns `managed<T, std::TracingCollector>`. Ownership
transfers on success. This is an unsafe operation: callers must supply an owned,
complete `Memory::Allocate` allocation. The library rejects duplicate and
already-managed overlapping addresses, but cannot validate arbitrary pointers,
stack storage, interior pointers into unknown native allocations, or foreign
allocator origins. Failure leaves ownership unchanged. Raw aliases must not
destroy or free successfully adopted storage. The compiler has no special
recognition of this method.

`ReferenceAdded(void* slot) nothrow` reports initialized reference storage;
`ReferenceRemoved(void* slot) nothrow` retires it before overwrite or lifetime
end. Callbacks must neither collect, construct managed values, nor throw. The
default provider scans the latest pointer in each live registered slot; ordinary
bytes are never conservatively scanned. Native/local slots are roots. Slots
inside managed allocations are edges followed only when their owner is reachable.
Adopting an object automatically changes that classification at collection time.
Cycles without external roots are reclaimed.

Native containers such as `Vector<T^>` must end their managed element lifetimes
before freeing storage. The compiler emits those slot callbacks for normal
construction, assignment, movement, and destruction. The collector retires slots
inside reclaimed managed objects itself. `Memory::Allocate` and `Memory::Free`
perform no hidden collector tracking. Manually freeing an owned managed object
through a raw alias is invalid; there is no runtime interception protecting it.

All lists, ownership checks, marking, and sweeping live in `GC.gf`. Private inline
LLVM supplies a mutable global state slot until the language has static storage,
a thread-local identity with an atomic owner check, and an unaligned pointer
load. Metadata uses the host allocator directly so it cannot recursively invoke
managed allocation. Payload reclamation calls the selected `Memory::Free`;
a deallocator failure terminates through the normal nothrow boundary. Metadata
bytes are excluded from `ManagedBytes`.

The default provider has no compaction, finalization, pinning, or cross-thread
collection. Access from a second thread terminates. Pointees cannot contain
resources requiring destructors. Other providers need not share this collector's
allocation algorithm or collection threshold. Moving references and thread
rendezvous are not yet implemented by the compiler.

`examples/managed` demonstrates a managed cyclic graph retained by a native Vector
and reclaimed after its elements are cleared.
