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
custom allocator. This default requires libc. A `NoStdLib` program can select
individual core sources and supply its own memory hooks and native startup;
see [the freestanding example](../../docs/FREESTANDING_CORE.md).

## Console

`Console/Console.gf` provides C-string and caller-buffer APIs. It needs `core/IOException.gf`,
`core/EndOfStreamException.gf`, and `core/BufferTooSmallException.gf`, but does not require `String.gf`.
`Console/ConsoleString.gf` adds the optional owning-string conveniences. Referencing
`std/std.gfproj` includes both.

`Console/StandardWriters.gf` adds the read-only `Console::Out` and
`Console::Error` namespace properties, exposing borrowed process-lifetime
`ITextWriter*` values. Their getters are `nothrow`. They accept counted writes,
including embedded zeros, and can be passed directly to formatting APIs without
constructing an intermediate String:

```csharp
using std;
int main()
{
    int value = 42;
    value.Format(Console::Out);
    Console::Error.Write("warning", 7);
    Console::Flush();
    Console::FlushError();
    return 0;
}
```

These pointers borrow private static writer values; never delete or explicitly
destroy them. No caller-managed lifetime or heap allocation is involved.
Each write resolves the current standard stream, so existing CRT redirection is
respected. `Flush()` flushes stdout and `FlushError()` flushes stderr; write and
flush failures throw `std::IOException`. A multi-write formatting operation may
already have produced partial output before a failure. Separate formatting calls
can interleave across threads according to libc's stream synchronization.
Nonempty writes check both the returned byte count and the stream error indicator:
some libc buffering paths report a full byte count despite an internal flush
failure. This does not force a flush; buffered failures can still appear only at
an explicit flush or close.

The standard-stream accessor bridge in `Runtime/Stdio.cpp` exposes stdin, stdout,
and stderr because these are C macros rather than portable external variables.
The actual writer behavior remains gflat code.

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
- `Read(data, count)` reads up to the requested count into caller storage and
  returns the number of bytes obtained. It may block until the count is filled
  or EOF/error occurs. Zero count performs no input access or consumption.
- `ReadByte()` returns the next value in 0-255, or -1 at EOF. Input errors throw
  `std::IOException` rather than being mistaken for EOF.
- `PeekByte()` has the same result/error convention without consuming the next
  byte. Repeated peeks return the same value; subsequent counted and line reads
  see that byte. It uses libc's one-byte pushback, performs no application
  allocation, and respects the existing stdin text mode described below.
  A peek and a following read are separate operations; callers sharing stdin
  between threads must coordinate them.
- `ClearInputError()` clears stdin's sticky EOF and error indicators, like
  `File.ClearError()`. It neither consumes/discards pending input nor repairs a
  failed descriptor or changes the stream mode. Clear the state before retrying
  after a recoverable error, or after more data becomes available on a stream
  that had reached EOF. An unchanged underlying failure still fails on retry.
- `ReadExactly(data, count)` throws `std::EndOfStreamException` if EOF arrives
  before the count is satisfied; the available prefix has already been consumed
  and written. Other input errors remain `std::IOException`.
- Counted and byte reads do not append a terminator, perform line processing,
  allocate a String, close stdin, or change its mode. They share the current
  stdin position with line readers. The host CRT's existing mode still applies:
  Windows normally translates CRLF to LF and treats Ctrl+Z as EOF in text mode,
  even for redirected stdin. These are not a promise of binary-mode input.
- `TryReadLine(data, capacity, ref length)` uses caller-owned storage without
  calling `Memory::Allocate` on the normal path. Capacity includes room for the null terminator;
  zero capacity throws `BufferTooSmallException` before reading or touching the
  buffer. The caller provides valid writable storage.
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
not copy the string or allocate. On read or allocation errors the reused String
remains valid and can contain a partial line. Input may stop partway through
that line; the next read resumes at its remaining suffix. Unlike bounded-buffer
overflow, these failures do not promise to consume the rest of the line.

These APIs operate on bytes through libc's standard text streams, including its
platform newline translation. They do not select the Windows console code page.
The compiler links the small native standard-stream accessor only when referenced;
`stdin` and `stdout` are macros rather than portable external variable names.
`gflat --run` inherits console input/output, so flushed prompts appear immediately.

## Files

`File.gf` provides a move-only owning `std::File` for binary byte IO. It needs
`core/IOException.gf` and `core/EndOfStreamException.gf`; it does not depend on
`String` or allocate a gflat buffer.
`File` implements the automatically available `ITextWriter` contract, so
primitive and user formatting can write directly through a borrowed `File*`.
This adds no fields or ownership transfer; a File remains one native stream
pointer in size. With `NoStdLib`, supply `core/Text.gf` explicitly alongside
the other selected sources.
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
- `OpenTemporary()` owns a libc `tmpfile` binary read/write stream. The runtime
  selects its location; no filename is exposed. Explicit `Close` and scope
  destruction release it and remove its temporary backing file. Normal process
  exit also invokes libc's cleanup, but abrupt termination is runtime/platform
  dependent. Creation can fail due to permissions, runtime limits, or storage.
  The current File API does not expose seeking/rewinding, including on temporary
  streams; writing advances the position as usual.
- `Read(data, capacity)` returns the bytes read. Zero at EOF is normal; IO
  failures throw `IOException`. Reads do not add a null terminator.
- `ReadExactly(data, count)` fills the requested region or throws
  `EndOfStreamException` on unexpected EOF. Actual read errors throw `IOException`.
  On a truncated file the available prefix has already been consumed and stored.
- `ReadByte()` returns an integer from 0 through 255, or -1 at EOF; read errors
  throw. `WriteByte(char)` writes exactly one byte, including zero.
- `PeekByte()` has the same result/error convention without consuming the byte.
  Repeated lookahead sees the same next byte. Looking ahead at EOF observes EOF.
- `EndOfFile` reports libc's observed EOF indicator, not whether a future read
  will reach the end. Reading exactly the remaining bytes need not set it;
  a subsequent failed read does. `ClearError()` resets EOF and error indicators
  without changing the position or fixing the underlying IO problem. These
  operations throw `IOException` on closed files.
- `CopyTo(destination, buffer, capacity)` borrows a `File*` and caller-owned
  buffer, returning the copied byte count. It starts at the current position,
  leaves both streams open, and rejects zero capacity and the same `File`
  instance as both source and destination. Separate handles to the same
  underlying file are not detected; callers must avoid that combination.
  `CopyTo(destination)` uses a 4096-byte stack buffer. Callers must not modify
  either stream concurrently. Copying is not transactional: failures can leave a
  partial destination, and the source may have advanced.
- `CopyTo(ITextWriter*, buffer, capacity)` and its stack-buffer overload stream
  to other writers, including `Console::Out`, `String`, and `BufferWriter`.
  No intermediate owning value is created. Both source and destination remain
  borrowed; empty input performs no writes. Passing this same File through an
  interface view is rejected before reading. Wrappers that write back into the
  source file cannot be detected and must be avoided. Sink exceptions propagate
  unchanged after the current source chunk has been consumed; already written
  output remains. The typed `File*` overload additionally validates a closed
  destination before consuming input; a generic interface sink is validated
  when its `Write` method runs.
- `Write(data, count)` preserves embedded zero bytes; `Write(text)` stops at
  the first zero. Partial IO may already have occurred when an error is thrown.
  Nonempty writes also reject a set libc stream error indicator, even if libc
  reports the full count. After handling an IO error, use `ClearError` only when
  intentionally retrying after correcting the underlying problem; writes do not
  automatically clear errors or retry partially written data.
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

### Owning binary and text conveniences

`File/FileBytes.gf` adds `std::FileBytes` for owning binary data, requiring core
`Vector`, `Span`, and their enumerable contracts, but no String or GC sources.
`ReadAll(path)` returns a `Vector<byte>` owned by the caller. Reads use a stack
chunk and geometric vector growth; an empty file returns an empty vector without
allocating. Embedded zeros and all byte values are preserved, with no terminator,
encoding conversion, or BOM handling. Allocation/read failures release the partial
vector and close the file before propagating.

`WriteAll(path, data, count)` replaces a file and
`AppendAll(path, data, count)` appends, taking counted `readonly(byte)*` data.
Zero count still creates/opens the file. Both close explicitly to report delayed
write errors. A write failure can leave partial output; these are not atomic
file-replacement operations. Caller-buffer code can continue using `File::Read`
and `File::Write` directly without the owning convenience source.

`File/FileText.gf` adds `std::FileText`, requiring the core `String` facilities.
`FileText::ReadAll(path)` returns an owning `String`; embedded zeros are retained.
It reads raw bytes without validating UTF-8, removing a BOM, or translating
newlines. Memory grows with the file; use `File::Read` or `CopyTo` for bounded
storage. `WriteAll` truncates and `AppendAll` appends; both accept a C string,
counted bytes, or `ref readonly(String)`. These helpers explicitly close the
file so final flush/close failures are reported. They are not atomic updates.

### Reading lines

`File/FileLines.gf` provides `std::FileLines`, borrowing a `File*` without closing
it. Include `core/EndOfStreamException.gf` and `core/BufferTooSmallException.gf`.
`TryReadLine(input, data, capacity, ref length)` reads into caller storage and
returns false only at EOF before any input. Capacity includes the null
terminator. LF and CRLF end a line; a bare CR and embedded zero bytes remain
part of its counted contents. A final unterminated line is returned normally.
An empty terminated line returns true with length zero.

On overflow the entire line is consumed, the terminated prefix and its length
are retained, then `BufferTooSmallException` is thrown. Zero capacity throws
before reading. `ReadLine(input, data, capacity)` returns the length and throws
`EndOfStreamException` when no line remains.

`TryReadLine(input, ITextWriter* output)` appends a line to any writer. For
example, pass the address of an owning `String` and call `Clear()` between lines
to reuse its allocation. This overload does not clear the output. Errors from
the source or output can leave a partially consumed line; no rollback is
promised. Reading uses libc's existing stream buffering, with a 256-byte stack
chunk for batched writer calls and no additional gflat heap allocation.

`File/FileLineSequence.gf` adds `FileLineSequence` and `.Lines()` on `File*` for
lazy enumeration and existing query operators. Its iterator owns and reuses one
String buffer, while the file stays borrowed and open. Each enumeration consumes
the shared current file position; it does not reopen or rewind the file.
`Current` borrows the current String through a readonly reference; copying a line
or calling `ToVector()` creates independent owning values. See
[borrowed file lines](../../docs/FILE_LINE_SEQUENCE.md).

### Binary integers

`File/FileBinary.gf` supplies `std::FileBinary`, borrowing `File*` streams. Its
`ReadUInt16LittleEndian`, `ReadUInt32LittleEndian`, and `ReadUInt64LittleEndian`
methods read fixed-width unsigned values; the corresponding `BigEndian` methods
use network byte order. Matching `WriteUInt16/32/64LittleEndian` and
`WriteUInt16/32/64BigEndian` methods write these representations. Signed
`ReadInt16/32/64` and `WriteInt16/32/64` methods are available in both byte orders
and preserve two's-complement bits. Each operation
uses an eight-byte stack buffer and works independently of host byte order,
alignment, pointer width, and the C runtime's `long` size. Truncation throws
`EndOfStreamException`; actual IO errors throw `IOException` (the base type of
`EndOfStreamException`), with the same partial-progress behavior as
`ReadExactly` and `Write`. Writes do not flush or close the stream.

## Managed memory

`TracingCollector.gf` defines `std::TracingCollector`, a single-threaded,
non-moving collector written in gflat. `GC.gf` supplies the default global
binding as a constrained, replaceable alias; reference `std/std.gfproj` or include
these sources and their dependencies explicitly:

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

All lists, ownership checks, marking, and sweeping live in `TracingCollector.gf`.
Collector state uses an ordinary private static nullable pointer field. Private
inline LLVM supplies thread-local identity, an atomic owner check, and an
unaligned pointer load. Metadata and payload allocation use the selected
`Memory::Allocate` and `Memory::Free` hooks. These hooks must not reenter this
collector, including through managed allocation; recursive entry is not prevented
by a separate allocator. A deallocator failure terminates through the normal
nothrow boundary. Metadata bytes are excluded from `ManagedBytes`.

The default provider has no compaction, finalization, pinning, or cross-thread
collection. Access from a second thread terminates. Pointees cannot contain
resources requiring destructors. Other providers need not share this collector's
allocation algorithm or collection threshold. Moving references and thread
rendezvous are not yet implemented by the compiler.

`examples/managed` demonstrates a managed cyclic graph retained by a native Vector
and reclaimed after its elements are cleared.

## Environment variables

`Environment/Environment.gf` provides `std::Environment::GetVariable(name)`,
returning a nullable pointer to read-only C-string bytes from libc `getenv`.
Null means missing. A nonnull pointer whose first byte is zero means the variable
exists with an empty value. Names are null-terminated too: bytes after a zero in
the supplied name do not participate in lookup.

The C runtime owns this storage. Never free or modify it. The pointer can become
invalid after subsequent environment operations, and the exact invalidation
rules depend on the runtime. Copy values that need to outlive such operations.
Names, values, case sensitivity, encoding, and backend limits follow libc; this
is not a portable Unicode environment API on Windows.

The borrowed source does not require String or gflat's memory/exception runtime.
Its wrapper performs no allocation through `Memory`; libc itself may lazily
initialize environment storage. Both its wrapper and private native declaration
are explicitly `nothrow`.

`Environment/EnvironmentText.gf` separately provides owning conveniences:

- `EnvironmentText::GetVariable(name)` returns a copied `String`; a missing
  variable throws `Exception`, while an existing empty value returns an empty
  String.
- `EnvironmentText::TryGetVariable(name, ref output)` returns false and clears
  output when missing. On success it replaces output with an independent owning
  copy. If allocating that copy fails, the previous output remains unchanged.

Owning copies can be freely modified without altering the process environment.
Neither API synchronizes with external code modifying the environment; callers
must coordinate concurrent changes, including during copying. There is no
setter or process-spawning API in this layer.
