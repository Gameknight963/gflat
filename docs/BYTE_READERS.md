# Borrowed byte readers

`Standard::IO::IByteReader` is a source of raw counted bytes. `Read(char* data, nuint count)`
returns how many bytes were written into caller storage. Embedded zero and high-bit
bytes have no special meaning. Nothing is decoded or zero-terminated.

For a positive request, zero means EOF. A short positive read is normal and must not
be treated as EOF. This is a blocking protocol: a nonblocking source must not report
"would block" as zero. A zero-byte request consumes nothing and must not dereference
the buffer, but can still reject a closed or otherwise unusable source.

`File` and optional `Windows::TcpClient` implement this interface without transferring
their ownership. Keep each concrete value alive, unmoved and open for every call
through its borrowed interface pointer. The interface does not add closing, seeking,
length or ownership semantics.

## Helpers

Import `Standard::IO` for these extensions on `IByteReader*`:

- `ReadByte()` returns one byte as an int from 0 through 255, or -1 at EOF.
  It makes one positive one-byte read, rejects over-reported counts, and propagates
  source errors unchanged. Byte 255 is data and is never confused with EOF.
- `ReadExactly(data, count)` loops through short reads and throws `EndOfStreamException`
  if EOF arrives before `count`. A zero-byte operation calls the source once to preserve
  its state validation. Reader exceptions propagate unchanged.
- `SkipExactly(count)` discards exactly that many bytes through bounded reads,
  using 4096 bytes of stack scratch. It works without seeking, preserves the same
  zero-count validation, and throws `EndOfStreamException` on early EOF. Requests
  never extend beyond the remaining count. A buffered source may read ahead;
  continue consuming through that same adapter to retain its unread bytes.
- `CopyTo(output, scratch, capacity)` rejects empty scratch, reads until EOF, and sends
  each counted chunk to `ITextWriter*`. It returns the number of bytes sent. A source
  reporting more bytes than requested or a total count overflow throws `IOException`
  before forwarding that chunk.
- `CopyTo(output)` supplies 4096 bytes of stack scratch.

The helpers retain no endpoint or buffer and allocate no intermediate data. They do
not close, flush, rewind, or call an encoder/decoder's `Complete`. Input, output and
scratch must be logically distinct, with no overlap between scratch and either
endpoint's state or retained backing bytes. Do not copy a stream into itself, even
through different adapters. The generic helper cannot establish endpoint identity.
Existing concrete `File::CopyTo` validation remains unchanged.

Failure can leave consumed input, a modified output-buffer prefix, or already-written
chunks. There is no rollback or automatic draining. A reader can fail after consuming
an unknown prefix, so callers cannot infer consumed bytes from an exception alone.

## Read counted lines from any byte source

`TryReadLine(output)` appends one line to an `ITextWriter` without clearing it.
`TryReadLine(ref String line)` clears the String first and reuses its capacity,
matching Console's existing convention. Immediate EOF returns false with an empty
String. `ReadLine()` returns a new owning String and converts immediate EOF to
EndOfStreamException. A blank terminated line is a successful empty line.

LF and CRLF are removed; a bare CR, embedded zero, and high bytes remain counted
data. Final nonempty unterminated lines are returned once. No encoding conversion
or validation is performed. Console.In still observes its existing host stream
translation before these byte rules apply.

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

The helper requests one source byte at a time and does not consume the byte after
LF. Keep one BufferedReader across calls when buffering is useful; read any later
bytes through that same object so its read-ahead tail remains available. The
helper owns no heap buffer, closes nothing, and writes counted stack chunks to the
sink. String and BufferedReader retain their normal allocation and ownership rules.

Source and writer exceptions propagate unchanged. Consumed input and prior output
are not rolled back. An uncommitted scratch prefix may be lost on error; the
String convenience has already cleared its old value and may contain an appended
prefix. Failure does not automatically drain the rest of the line. Existing
BufferWriter provides a raw-buffer route, but an over-capacity Write can stop
midline; this differs from Console's explicitly draining raw-buffer overload.

Keep the concrete input/sink alive and unmoved, with disjoint state and captured
storage. In particular, the input must not borrow the String output's old buffer,
which clearing or growth can change. Borrowed views of the reused String's old
contents cannot be relied on across a call. The writer helper is independently
available in `Standard/core/IO/byte_reader_lines.gf`; the optional String overloads
are in `byte_reader_string_lines.gf`.


## Decode file or network input through the same consumer

The existing strict `HexDecoder` is an `ITextWriter`. It accepts arbitrary chunk splits
and checks the final tail in `Complete`, so the consumer needs no entire-input string.
Use distinct source and destination files: opening the destination truncates it.

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
    File source = File::OpenRead(sourcePath);
    File destination = File::OpenWrite(destinationPath);
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

With the optional Windows network project referenced, the same `DecodeHexFrom` accepts
`&client` for an open `Windows::TcpClient`. For a TCP stream, its peer must end sending
for EOF-based copying to finish. A persistent connection carrying multiple messages
needs a separate bounded framing protocol; `CopyTo` does not know message boundaries.
[LimitedReader](../Standard/core/IO/LIMITED_READER.md) exposes one payload as a
bounded source. To discard its unread allowance explicitly, call
`frame.SkipExactly(frame.Remaining)`; early EOF still reports a truncated payload.
