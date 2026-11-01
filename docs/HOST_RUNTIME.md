# Host runtime facilities

Standard's libc layer supplies console IO, files, environment variables, default
allocation, and process termination. This reference groups the APIs by use.

## Console

### Reading and writing

| Call | Result |
| --- | --- |
| `Write(text)` | Writes a C string |
| `Write(data, length)` | Writes counted bytes, including embedded zeros |
| `WriteLine()` | Writes a newline |
| `WriteLine(text)` / `WriteLine(data, length)` | Writes text followed by a newline |
| `Write(value)` / `WriteLine(value)` | Formats a primitive value |
| `Write(ref value)` / `WriteLine(ref value)` | Borrows an `IFormattable` value for output |
| `Flush()` / `FlushError()` | Flushes stdout or stderr |

See [formatted output](CONSOLE_FORMATTING.md) for primitive and custom values.

Write and flush failures throw `Standard::IO::IOException`. Earlier output can
already have reached the stream. Flush before reporting successful command-line
output so delayed write errors can affect the exit status.

Console operations follow the host stream's text mode. On Windows this normally
translates line endings and treats Ctrl+Z as EOF, including redirected input.
The active console code page controls the display of non-ASCII bytes.

### Standard writers

`Console::Out` and `Console::Error` expose process-lifetime `ITextWriter*` values:

```csharp
using Standard;

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

These borrowed writers resolve the current standard stream on each call, so they
follow CRT redirection. Keep a multi-write operation synchronized when several
threads share a stream.

### Local writer adapters

Use `Console::Writer` when a local stdout adapter is convenient:

```csharp
using Standard;

int main()
{
    Console::Writer output = new Console::Writer();
    int value = 42;
    value.Format(&output);
    Console::WriteLine();
    Console::Flush();
    return 0;
}
```

It borrows stdout. `Console::Flush()` controls flushing.

### Standard input

`Console::In` exposes a process-lifetime `Standard::IO::IByteReader*`. It uses the
same cursor as the direct Console read methods and follows CRT redirection.
Import `Standard::IO` to use the shared reader extensions, including
[`ReadToEnd`](READ_TO_END.md), `ReadExactly`, and `CopyTo`.

When adding a `BufferedReader`, route later reads through that adapter so its
prefetched bytes remain part of the input sequence.

#### Byte input

| Call | Result |
| --- | --- |
| `Read(data, count)` | Reads up to `count` bytes and returns the amount obtained |
| `ReadByte()` | Returns 0–255, or -1 at EOF |
| `PeekByte()` | Observes the next byte using libc's one-byte pushback |
| `ReadExactly(data, count)` | Fills the range or throws `EndOfStreamException` |
| `ClearInputError()` | Clears stdin's sticky EOF and error indicators |

Reads can block until the request is filled or EOF/error occurs. Byte input
errors throw `IOException`. A failed read can leave a consumed and modified
prefix. `ReadExactly` also retains the available prefix when EOF is early.

A zero-count read consumes nothing. Coordinate a peek and subsequent read when
sharing stdin. Before retrying a recoverable failure, correct the underlying
problem and clear its sticky stream state.

#### Line input

`TryReadLine` returns false at EOF before any bytes. Empty lines return true, and
a final unterminated line is returned normally. LF and CRLF end a line. Other
carriage returns remain content. These rules apply after host text translation.

For caller storage:

```csharp
int main()
{
    Console::Write("Name: ");
    Console::Flush();
    char[128] buffer;
    nuint length = Console::ReadLine(buffer, 128);
    Console::Write("Hello, ");
    Console::WriteLine(buffer, length);
    Console::Flush();
    return 0;
}
```

`TryReadLine(data, capacity, ref length)` writes a terminated line prefix. Capacity
includes its terminator. `ReadLine(data, capacity)` returns the length or throws
`EndOfStreamException` at EOF.

- Zero capacity throws `BufferTooSmallException` before reading
- An oversized line is consumed completely, then `BufferTooSmallException` is
  thrown with the terminated prefix and its length retained
- Other read failures can stop partway through a line

To stream a line into a writer, use `TryReadLine(ITextWriter* output)`. It appends
the line's counted bytes. An empty line produces no writer call. A writer failure
propagates after input may have advanced beyond the accepted output. The next
read resumes where input stopped.

For example, count a line's bytes while it is read:

```csharp
using Standard::Text;
using Standard;

int main()
{
    CountingWriter length = new();
    while (true)
    {
        length.Clear();
        if (!Console::TryReadLine(&length)) break;
        length.Length.Format(Console::Out);
        Console::WriteLine();
    }
    Console::Flush();
    return 0;
}
```

### Owning console strings

`ReadLine()` returns an owning String and throws at EOF. `TryReadLine(ref line)`
clears and reuses a String's capacity:

```csharp
using Standard::Text;
using Standard;

int main()
{
    String line = new String();
    while (Console::TryReadLine(ref line)) Console::WriteLine(ref line);
    return 0;
}
```

Read and allocation failures leave the reused String valid with any completed
prefix. Input can stop partway through the line. `Write(ref immutable(String))`
and `WriteLine(ref immutable(String))` use its counted contents.

### Console source selection

| Source | Provides |
| --- | --- |
| `libc/Console/Console.gf` | Direct console operations |
| `libc/Console/standard_writers.gf` | `Out` and `Error` |
| `libc/Console/standard_readers.gf` | `In` |
| `libc/Console/Writer.gf` | Local stdout writer |
| `libc/Console/console_string.gf` | String conveniences |

Selective builds also include the relevant IO exception and reader/writer
contracts. The C bridge in `Runtime/Stdio.c` exposes the standard streams, which
are macros in C. See [source selection](PROJECTS.md#standard-library-selection).

## Files

`Standard::IO::FileStream` is a move-only binary stream. Default construction is closed.
`File::OpenRead`, `OpenWrite`, `OpenAppend`, and `OpenTemporary` create streams.
Scope destruction closes it. Call `Close()` explicitly to report final flush
and close errors:

```csharp
using Standard;
using Standard::IO;

int main()
{
    FileStream output = File::OpenWrite("message.txt");
    output.Write("Hello\n");
    output.Close();
    return 0;
}
```

### Opening files

| Factory | Mode |
| --- | --- |
| `OpenRead(path)` | Reads an existing binary file |
| `OpenWrite(path)` | Creates or truncates a binary file |
| `OpenAppend(path)` | Appends, creating a file when needed |
| `OpenTemporary()` | Owns a libc `tmpfile` binary read/write stream |

Paths use libc's native narrow-character encoding. On Windows, that follows the
configured CRT code page. `FileHandle` is the opaque libc `FILE*` alias used
internally. Its lifetime belongs to FileStream and libc.

#### Temporary files

Closing a temporary stream removes its backing file. Normal process exit also
runs libc cleanup. Abrupt termination follows the host's cleanup behavior.

Temporary streams follow libc's input/output direction rules. Flush before
switching from output to input. A switch from input to output requires positioning
unless the read reached EOF. FileStream currently exposes the EOF transition but has
no seek operation. Flushing leaves the cursor at its existing position.

### Reading and cursor state

| Method | Result |
| --- | --- |
| `Read(data, capacity)` | Byte count, with zero at EOF |
| `ReadExactly(data, count)` | Fills the requested region or throws at early EOF |
| `SkipExactly(count)` | Consumes and discards the requested byte count |
| `ReadByte()` | Integer 0–255, or -1 at EOF |
| `PeekByte()` | Observes the next byte |
| `EndOfFile` | The stream's observed EOF indicator |
| `ClearError()` | Clears EOF and error indicators at the current position |

IO failures throw `IOException`. Early EOF in `ReadExactly` or `SkipExactly`
throws `EndOfStreamException` after consuming the available prefix. Closed files
reject reads, including zero-count reads. `SkipExactly` uses a 4096-byte scratch
buffer to discard sequential input.

`EndOfFile` becomes true after a read encounters EOF. Consuming exactly the
remaining bytes can leave it false until another read is attempted.

### Copying streams

`CopyTo(destination, buffer, capacity)` copies to a borrowed FileStream or `ITextWriter`
and returns the copied count. The overload without scratch uses a 4096-byte stack
buffer. Both streams remain open.

Zero capacity and the same FileStream as source and destination are rejected. Keep the
underlying files distinct even when accessed through separate handles or wrappers.
A typed FileStream destination is checked for closure before input is consumed. A
generic writer validates its own state when called.

A failure can leave partial output and an advanced input cursor. Sink exceptions
propagate after the current input chunk has been consumed.

### Writing and closing

| Method | Behavior |
| --- | --- |
| `Write(text)` | Writes through the first zero |
| `Write(data, count)` | Writes counted bytes |
| `WriteByte(value)` | Writes one byte |
| `WriteLine(...)` | Writes contents followed by LF on every platform |
| `Flush()` | Reports delayed write errors |
| `Close()` | Closes and reports final IO errors. Repeated close is harmless |
| `IsOpen` | Reports stream ownership |

Writes can fail after partial output. After correcting a recoverable failure,
clear the stream error state before explicitly retrying. A failed Close leaves
the object closed. Destruction attempts closing without reporting errors.

`File::Remove(path)` and `File::Rename(source, destination)` throw `IOException`
on failure. Rename and destination replacement follow the host C runtime's rules.

### Owning binary and text conveniences

| API | Owned result or input |
| --- | --- |
| `File::ReadAllBytes(path)` | `Vector<byte>` |
| `File::ReadAllText(path)` | `String` |
| `File::WriteAllText(path, ...)` | Replaces file contents with text bytes |
| `File::WriteAllBytes(path, ...)` | Replaces file contents with bytes |
| `File::AppendAllText(path, ...)` | Appends text bytes |
| `File::AppendAllBytes(path, ...)` | Appends bytes |

These helpers preserve file bytes, including embedded zeros, line endings, and
BOMs. String results keep the same bytes. Reads close the file before returning.
Write helpers close explicitly so delayed errors propagate.

`WriteAllBytes` and `AppendAllBytes` accept counted bytes or `ref immutable(Vector<byte>)`.
The text overloads accept C strings, counted bytes, or `ref immutable(String)`. Empty
writes still open the file, so a whole-file write creates or truncates it. Failed writes
can leave partial file contents.

#### Read limits

Both whole-file read methods accept `maximumBytes`. An exact-length file succeeds.
A longer file throws `IOException("File exceeds maximum byte count")` after an
extra-byte EOF check. A zero limit accepts only an empty file.

The limit counts content bytes. String's terminator, spare capacity, and CRT
buffering add storage overhead. On read, limit, or allocation failure, partial
results are destroyed and the file closes. An existing assignment target remains
unchanged. Allocation failure throws `BadAllocException`.

### Selecting file sources

`FileStream.gf` provides stream IO with the byte-reader and IO exception sources.
The static `File` facade adds whole-file String and Vector operations, so selecting
`File.gf` also requires those types and their dependencies. Low-level code can call
`FileStream::OpenRead`, `OpenWrite`, `OpenAppend`, and `OpenTemporary` directly.

### File digests

`FileHash::SHA256` and `FileHash::SHA512` return `byte[32]` and `byte[64]` digests.
Path overloads open and close a file. Borrowed `FileStream*` overloads hash from the
current position to EOF and leave the stream open. See [file hashing](FILE_HASH.md).

### Reading lines

`FileLines::TryReadLine(input, data, capacity, ref length)` reads through a borrowed
FileStream. It uses the same LF/CRLF and counted-buffer rules as Console line input.
Zero capacity throws before reading. An oversized line is drained before
`BufferTooSmallException`, retaining the terminated prefix. `ReadLine` throws
`EndOfStreamException` at EOF.

`TryReadLine(input, ITextWriter* output)` appends a line. Source or sink failures
can leave partial progress. Reuse a String by clearing it between calls.

`FileStream.Lines()` returns a lazy sequence whose iterator reuses one String buffer.
Its `Current` returns `ref immutable(String)`. Each enumeration consumes the
current file position. Copying a line, or calling `ToVector()`, preserves an
independent value. See [file line iteration](FILE_LINE_SEQUENCE.md).

### Binary numbers

`FileBinary` provides little- and big-endian reads and writes for 16-, 32-, 64-,
and 128-bit integers, plus IEEE-754 float and double. Floating representations
preserve signed zero and NaN bits. Reads and writes start at the current cursor.
Truncation throws `EndOfStreamException` after any available prefix.

The adapter uses the [shared binary IO extensions](BINARY_IO.md). `FileVarInt`
provides [canonical base-128 integer IO](FILE_VARINT.md).

## Environment variables

| API | Missing variable | Present variable |
| --- | --- | --- |
| `Environment::GetVariable(name)` | Null | Borrowed C string |
| `EnvironmentText::GetVariable(name)` | Throws `Exception` | Owning String |
| `EnvironmentText::TryGetVariable(name, ref output)` | False and clears output | True and replaces output with an owning copy |

An existing empty value is present. Names, encoding, and case sensitivity follow
libc. Borrowed values can be invalidated by later environment operations. Copy a
value when it must survive those operations. Coordinate concurrent environment
changes, including while copying.

Allocation failure in `TryGetVariable` preserves the old output.

## Allocation

The weak `Memory::Allocate` and `Memory::Free` defaults delegate to libc. Replace
both to select another allocator. A custom runtime can select individual core
sources with `NoStdLib`. See [freestanding builds](FREESTANDING_CORE.md).

## Unhandled exceptions

`Exceptions::Unhandled(Exception*? error)` calls libc `exit` with the exception's
`GetCode()` or 1 for a foreign exception. Exception codes default to 1. Replace
the hook to report the error and terminate. If the hook returns or throws, the
runtime exits with status 1.

## Floating-point text

`Standard::Format` uses `snprintf` with 9 significant digits for float and 17 for
double. Parsing uses `strtof` and `strtod`. These operations follow the current
C locale. See [floating-point parsing](FLOATING_PARSING.md).

## Managed memory

The global constrained `GC` alias selects the managed-reference provider:

```csharp
public weak alias GC : Standard::IGarbageCollector = Standard::TracingCollector;
```

Replace it with `replace alias GC = MyCollector;`. `T^` abbreviates
`managed<T, GC>`. Explicit `managed<T, OtherCollector>` references retain that
collector identity, and `new^ T() with OtherCollector` selects it for construction.

`IGarbageCollector` requires static `Allocate`, `ReferenceAdded`,
`ReferenceRemoved`, and `Collect`. Allocation and reference-lifetime calls are
compiler-generated. Collection policy belongs to the provider.

`GC::Collect()` returns the reclaimed allocation count. The default
`TracingCollector` keeps objects at fixed addresses and permits one thread.
Access from another thread terminates. Managed pointees must be destructor-free.

### Allocation and adoption

`TracingCollector::Allocate(bytes, alignment)` obtains, registers, and zeroes
storage through `Memory::Allocate`. A zero-byte request uses one byte. Allocation
failure triggers one collection and retry, then throws `BadAllocException`.
Unsatisfied alignment and registration failure free the pending allocation.
A failed constructor leaves its allocation collectible once the temporary root ends.

`TracingCollector::Adopt<T>(storage)` accepts a complete, already constructed
`Memory::Allocate` allocation and transfers it to managed ownership. It returns
`managed<T, TracingCollector>`. Failure leaves ownership with the caller.
After success, raw aliases must leave deletion to the collector.

Adopt rejects duplicate and already-managed overlapping allocations. Correct
allocation origin, complete-object storage, and destructor-free pointees remain
the caller's responsibility.

### Reference lifetimes

`ReferenceAdded(slot)` registers an initialized managed-reference location.
`ReferenceRemoved(slot)` retires it before overwrite or lifetime end. Both are
`nothrow`. Hook implementations must avoid collection and managed construction.

The default collector follows registered native/local slots as roots and follows
slots inside managed allocations when their owner is reachable. Unrooted cycles
are reclaimed. Native containers such as `Vector<T^>` end element lifetimes
before releasing their storage. The compiler emits the corresponding slot calls
for construction, assignment, movement, and destruction.

### Collector implementation

`TracingCollector::CollectionThreshold()` defaults to 65536 newly allocated bytes.
A qualified weak replacement can change it. Zero disables allocation-triggered
collection. Explicit Collect calls remain available.

`TracingCollector::ManagedCount` and `ManagedBytes` report concrete-provider state.
Use the concrete type to access these members, which are outside the common
collector interface. ManagedBytes excludes bookkeeping allocations.

The implementation uses ordinary gflat state and private LLVM operations for
thread identity, an atomic owner check, and unaligned pointer loads. Its Memory
hooks must avoid reentering the collector. See the
[managed graph example](../examples/managed/README.md).
