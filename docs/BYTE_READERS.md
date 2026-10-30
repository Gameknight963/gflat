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

Use [BufferedReader](BUFFERED_READER.md) to coalesce small reads, or
[LimitedReader](../Standard/core/IO/LIMITED_READER.md) to bound a payload.
Deferred [byte enumeration](#enumerate-bytes-without-owning-the-source) and
[line enumeration](#enumerate-lines-without-owning-the-source) use this same protocol.

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

## Enumerate bytes without owning the source

`EnumerateBytes(input)` (or `input.EnumerateBytes()`) returns a `ByteSequence`.
The factory and `GetEnumerator()` perform no reads. Each successful `MoveNext()`
consumes one byte; `Current` returns that byte without further input. EOF ends that
iterator permanently. `Current` throws `IOException` before the first successful
advance, after EOF, or after a failed advance.

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

The iterator and query above share the same borrowed cursor: neither restarts the
input. `Take(0)` reads nothing, and `Take(2)` stops without probing the byte after
the second match. A filter may consume nonmatching bytes first. Direct foreach is
also supported: `foreach (byte value in view) { /* consume value */ }`.

Keep the concrete reader, its backing bytes, and any caller scratch alive and
unmoved; keep an owning source such as File open. Sequence and iterator copies
share that source cursor, although each iterator retains its own Current value.
A second enumeration continues from the current source position. The sequence
adds no buffer, read-ahead, closing, or ownership. A source such as BufferedReader
can itself read ahead; consume the next logical byte through that same adapter,
as above, rather than reading its underlying source directly.

Source exceptions propagate unchanged, and malformed read counts raise IOException.
A failed advance invalidates Current but does not roll back consumed input. A later
MoveNext retries the source at its current position; an underlying poisoned reader
can still reject that retry. Successful byte enumeration allocates no buffer.

Basic foreach uses `ByteSequence.gf` and `byte_reader_enumerate_bytes.gf` without
the query library. The optional `AsEnumerable()` extension is separately defined
in `byte_sequence_queries.gf`; import `Standard::Queries` to use query operators.

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


## Enumerate lines without owning the source

`input.EnumerateLines()` creates a deferred `LineSequence` over any `IByteReader`.
It follows `TryReadLine`: LF and CRLF are removed, empty terminated lines are
yielded, and a final nonempty unterminated line is yielded. Bare CR, embedded
zeros and high bytes remain counted contents. A trailing LF does not invent an
extra empty line.

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

Creating the sequence or iterator does not read input or allocate a line buffer.
Each iterator owns one String and reuses its capacity between lines. Its
`ref immutable(String) Current` is available after successful MoveNext; it is
invalid before the first line, after EOF, and after an exception. A Current borrow
ends when MoveNext is called or the iterator is moved or destroyed. Use a normal
`String saved = iterator.Current` copy to retain independent contents.
`foreach (String line in lines)` likewise produces owning value copies.

The sequence borrows the source; it never closes, rewinds or owns it. Keep the
concrete reader alive, open and unmoved. Sequence copies and new iterators share
its cursor; avoid interleaving consumption when whole-line boundaries matter.
Copying an iterator copies its current String into an independent owning snapshot
and shares the source cursor. Subsequent advancement of one copy does not replace
the other's current contents, but it consumes input that either could next read.
The parser consumes no byte beyond LF. A BufferedReader can prefetch, so retain
that same buffered reader to consume any remaining bytes, as above.

EOF is final for an individual iterator; further MoveNext calls do not read.
Source and String growth exceptions propagate unchanged and invalidate Current.
A later MoveNext retries from the source's then-current position and clears the
internal line first. Partially consumed input is not replayed or drained; a
partially collected prefix from the failed attempt is not yielded. Sources such
as BufferedReader retain their own error-latching policy.

`AsEnumerable()` is a separate optional query adapter in
`Standard/core/IO/line_sequence_queries.gf`. Queries that retain String values
make independent copies; a zero-allocation query chain is not promised.
Take(0) performs no reads. Early breaks and exceptions release the iterator's
String normally and leave the reader open at its consumed cursor.

The [distinct-lines tool](../examples/distinct-lines/README.md) combines a
caller-buffered stdin reader, LineSequence and HashSet<String> to preserve the
first occurrence of each counted line without copying duplicate keys into the set.

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
