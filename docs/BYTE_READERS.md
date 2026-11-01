# Borrowed byte readers

`Standard::IO::IByteReader` supplies raw bytes through `Read(char* data, nuint count)`.
FileStream, BufferedReader, and the optional Windows TcpClient implement it.

| Read result | Meaning |
| --- | --- |
| Positive | Number of bytes written, possibly fewer than requested |
| Zero for a positive request | EOF |
| Zero-count request | Consumes nothing, but source-state validation may still throw |

Keep the concrete reader alive, open, and at the same address while using its
interface pointer. Nonblocking implementations need a separate way to report
"would block", because zero means EOF here.

## Helpers

Import `Standard::IO` for these extensions:

| Method | Effect |
| --- | --- |
| `ReadByte()` | Returns 0–255, or -1 at EOF |
| `ReadExactly(data, count)` | Fills the requested region or throws `EndOfStreamException` |
| `SkipExactly(count)` | Discards the requested bytes or throws `EndOfStreamException` |
| `CopyTo(output, scratch, capacity)` | Copies until EOF and returns the number of bytes sent |
| `CopyTo(output)` | Uses 4096 stack bytes as scratch |

`CopyTo` requires nonempty scratch separate from both endpoints. Copying a stream
into itself is invalid, even through different adapters. Source errors can leave
input consumed and output partially written. When copying into an encoder or
decoder, call its `Complete()` afterward.

Use [ReadToEnd](READ_TO_END.md) to collect remaining bytes into a String,
[BufferedReader](BUFFERED_READER.md) for small reads, or
[LimitedReader](../Standard/core/IO/LIMITED_READER.md) for a bounded payload.

## Enumerate bytes without owning the source

`input.EnumerateBytes()` returns a deferred `ByteSequence`. Each `MoveNext()`
consumes one byte. `Current` caches that byte. A second enumeration continues
from the source's current position:

```csharp
using Standard::Binary;
using Standard::IO;
using Standard::Queries;

bool Even(byte value) => value % (byte)2 == (byte)0;

int main()
{
    byte[8] bytes = [0, 1, 2, 3, 4, 5, 6, 7];
    BinaryBufferReader source = new(bytes, 8);
    char[4] scratch;
    BufferedReader input = new(&source, scratch, 4);

    ByteSequence view = EnumerateBytes(&input);
    ByteSequence::Enumerator iterator = view.GetEnumerator();
    if (source.Position != 0 || input.BufferedCount != 0) return 1;
    if (!iterator.MoveNext() || iterator.Current != (byte)0) return 2;

    // Continue the same cursor: matches 2 and 4, stopping immediately after 4.
    if (view.AsEnumerable().Where(Even).Take(2).Count() != 2) return 3;
    if (ReadByte(&input) != 5) return 4;
    if (view.AsEnumerable().Take(0).Count() != 0) return 5;
    if (ReadByte(&input) != 6) return 6;
    return 0;
}
```

Copies share the source cursor. Keep reading through the same BufferedReader
when it has prefetched bytes. EOF permanently ends that iterator. An error
invalidates `Current`, and a later advance retries at the source's current
position, subject to its recovery rules.

`AsEnumerable()` enables query operators with `using Standard::Queries;`.
`Take(0)` reads nothing, and `Take(n)` stops after the nth match.

## Read counted lines from any byte source

| Method | Output and EOF behavior |
| --- | --- |
| `TryReadLine(output)` | Appends to an `ITextWriter` |
| `TryReadLine(ref String line)` | Clears and reuses the String |
| `ReadLine()` | Returns a String, throwing `EndOfStreamException` at immediate EOF |

Try forms return false at immediate EOF. A blank terminated line succeeds with
empty contents. LF and CRLF are removed. Bare CR and embedded NUL remain data.
A final nonempty unterminated line is returned once.

```csharp
using Standard::Binary;
using Standard::IO;
using Standard::Text;

int main()
{
    byte[11] bytes = [65, 13, 10, 66, 0, 67, 10, 108, 97, 115, 116];
    BinaryBufferReader source = new(bytes, 11);
    BufferedReader input = new(&source, 4);

    char[8] storage;
    BufferWriter output = new(storage, 8);
    if (!input.TryReadLine(&output) || output.Length != 1 || storage[0] != 'A') return 1;

    String line = s"old";
    line.Reserve(8);
    if (!input.TryReadLine(ref line) || line.Length != 3) return 2;
    if (line.Data[0] != 'B' || line.Data[1] != '\0' || line.Data[2] != 'C') return 3;

    String last = input.ReadLine();
    if (!last.Equals("last")) return 4;
    if (input.TryReadLine(ref line) || line.Length != 0) return 5;
    return 0;
}
```

The helper stops at LF. A BufferedReader may have prefetched later bytes, so use
that same cursor for subsequent reads. Input must not borrow the String being
cleared or grown.

Failure can stop midline after consuming input and writing a prefix. The remainder
is left unread. This differs from Console's raw-buffer overload, which drains
an overflowing line before throwing.

## Enumerate lines without owning the source

`input.EnumerateLines()` returns a deferred `LineSequence` with the same line rules.

```csharp
using Standard::Binary;
using Standard::IO;
using Standard::Text;
using Standard::Queries;
using Standard::Collections;

int main()
{
    byte[16] bytes = [65, 13, 10, 66, 0, 67, 10, 108, 97, 115, 116, 10, 84, 65, 73, 76];
    BinaryBufferReader source = new(bytes, 16);
    char[8] scratch;
    BufferedReader input = new(&source, scratch, 8);

    LineSequence lines = input.EnumerateLines();
    {
        // This iterator reuses its current String; observations do not copy it.
        LineSequence::Enumerator iterator = lines.GetEnumerator();
        if (!iterator.MoveNext() || iterator.Current.Length != 1 || iterator.Current[0] != 'A') return 1;
        String saved = iterator.Current; // An independent owning copy.
        if (!iterator.MoveNext() || iterator.Current.Length != 3 || iterator.Current[1] != '\0') return 2;
        if (saved[0] != 'A') return 3;
    }

    // A new enumeration continues the same cursor. Take stops after this line;
    // the unread tail remains available through the same buffered reader.
    Vector<String> next = lines.AsEnumerable().Take(1).ToVector();
    if (next.Count != 1 || !next[0].Equals("last")) return 4;
    if (input.ReadByte() != (int)'T') return 5;
    String tail = input.ReadLine();
    if (!tail.Equals("AIL")) return 6;
    return 0;
}
```

Each iterator owns one reusable String. Its `ref immutable(String) Current`
borrow lasts until the next advance, iterator move, or destruction. Copy the
String to retain a line. `foreach (String line in lines)` creates such copies.

New iterators and sequence copies share the source cursor. Iterator copies also
copy their current String, so each retains independent line contents while
advancing the shared input. Keep the reader alive and avoid competing consumers
when line boundaries matter.

EOF permanently ends an iterator. An error invalidates `Current`. A later advance
clears the partial line and retries from the consumed position. The
[distinct-lines example](../examples/distinct-lines/README.md) combines this
sequence with `HashSet<String>`.

## Decode file or network input through the same consumer

The consumer can accept any `IByteReader`:

```csharp
using Standard::IO;
using Standard::Encoding;

void DecodeHexFrom(IByteReader* input, ITextWriter* output)
{
    HexDecoder decoder = new(output);
    input.CopyTo(&decoder);
    decoder.Complete();
}

void DecodeFile(readonly(char)* sourcePath, readonly(char)* destinationPath)
{
    FileStream source = File::OpenRead(sourcePath);
    FileStream destination = File::OpenWrite(destinationPath);
    DecodeHexFrom(&source, &destination);
    destination.Close();
    source.Close();
}

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    DecodeFile(argv[1], argv[2]);
    return 0;
}
```

For an open `Windows::TcpClient`, pass `&client` to `DecodeHexFrom`. EOF-based
copying waits for the peer to finish sending. For a persistent framed connection,
wrap each declared payload in a LimitedReader. Discard its unread bytes explicitly
with `frame.SkipExactly(frame.Remaining)` when appropriate.

Sources: reader extensions and sequence types are in `core/IO`. Query adapters
are separate `byte_sequence_queries.gf` and `line_sequence_queries.gf` files.
