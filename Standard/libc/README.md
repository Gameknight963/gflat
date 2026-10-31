# Host runtime facilities

This part of Standard uses libc for console IO, files, environment variables,
floating-point text conversion, allocation, and process termination.
`Standard.gfproj` includes it alongside core.

## Console

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

- `Write` and `WriteLine` accept C strings or counted bytes
- `Read` and `ReadExactly` fill caller storage
- `ReadLine()` returns a String
- `TryReadLine(ref line)` reuses a String's capacity and returns false at EOF
- `Out` and `Error` expose borrowed text writers, and `In` exposes a byte reader

The host stream mode controls newline translation. Call `Flush()` when prompts
or final output must reach stdout. See [console APIs](../../docs/HOST_RUNTIME.md#console).

## Files

```csharp
using Standard; using Standard::IO;
int main()
{
    File output = File::OpenWrite("message.txt");
    output.Write("Hello\n");
    output.Close();
    return 0;
}
```

`File` owns a binary stream and closes it at scope exit. Call `Close()` explicitly
to report final flush and close errors.

- `FileText::ReadAll(path)` returns an owning String
- `FileBytes::ReadAll(path)` returns an owning `Vector<byte>`
- Both accept an optional maximum byte count
- `FileLines` reads into caller storage or a writer
- `File.Lines()` provides lazy line enumeration
- `FileBinary`, `FileVarInt`, and `FileHash` read structured values and digests

See [file APIs](../../docs/HOST_RUNTIME.md#files),
[file lines](../../docs/FILE_LINE_SEQUENCE.md), and [file hashing](../../docs/FILE_HASH.md).

### Owning binary and text conveniences

`FileText` reads and writes String values. `FileBytes` reads and writes byte
vectors. See [owning file helpers](../../docs/HOST_RUNTIME.md#owning-binary-and-text-conveniences).

### File digests

`FileHash` computes SHA-256 and SHA-512 digests from a path or borrowed file.
See [file hashing](../../docs/FILE_HASH.md).

### Reading lines

`FileLines` reads into a caller buffer or writer. `File.Lines()` provides lazy
iteration with a reusable String. See [line reading](../../docs/HOST_RUNTIME.md#reading-lines).

### Binary numbers

`FileBinary` and `FileVarInt` read and write fixed-width and variable-length
numbers. See [binary file IO](../../docs/HOST_RUNTIME.md#binary-numbers).

## Managed memory

`Standard::TracingCollector` is the default single-threaded, non-moving collector.
The global `GC` alias selects its implementation. Use `T^` for references managed
by that collector.

See [collector contracts and source selection](../../docs/HOST_RUNTIME.md#managed-memory)
and the [managed example](../../examples/managed/README.md).

## Environment variables

`Environment::GetVariable(name)` borrows a C string from the host runtime.
`EnvironmentText::GetVariable(name)` returns an owning String.
See [environment APIs](../../docs/HOST_RUNTIME.md#environment-variables).

## Allocation, exceptions, and floating-point text

- Replace `Memory::Allocate` and `Memory::Free` to select an allocator
- Replace `Exceptions::Unhandled` to report an unhandled exception and terminate
- Floating-point formatting and parsing use the host C locale

See [runtime hooks](../../docs/HOST_RUNTIME.md) and
[floating-point parsing](../../docs/FLOATING_PARSING.md).
