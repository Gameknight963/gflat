# Borrowed file lines

The optional `libc/IO/FileLineSequence.gf` source adds `FileLineSequence` and
the borrowed `File.Lines()` extension. It uses `IO/File.gf`, `IO/FileLines.gf`,
`String`, and the existing core sequence/query types.

For any IByteReader source, including BufferedReader, use
[LineSequence and EnumerateLines](BYTE_READERS.md#enumerate-lines-without-owning-the-source).
They provide the same reusable-current-line pattern without depending on File.

```csharp
using Standard::Text;
using Standard; using Standard::IO; using Standard::Queries; using Standard::Collections;

int main()
{
    File input = File::OpenRead("input.txt");
    File* source = &input;
    Vector<String> lines = source.Lines().AsEnumerable().ToVector();
    return (int)lines.Count;
}
```

`Lines()` constructs a small borrowed pointer view. `GetEnumerator()` creates an
iterator with an empty owned String. Neither operation reads the file or allocates
line storage. An ordinary empty-input enumeration also allocates no line storage.
The iterator factory accepts shallow readonly sequence views. It does not accept
deeply immutable views, since the new iterator can advance the borrowed file.
`MoveNext()` reads one line, reusing its String buffer on later calls. LF and CRLF
end a line and are removed. Bare CR and embedded null bytes remain counted data.
An unterminated final line is yielded, and an empty terminated line is a line.

## Ownership and file position

The sequence and its iterators borrow the **same File**, never copy or close it.
Keep the File alive, open, and at a stable address for their entire use. Closing
it early causes an I/O error. Destroying or moving its owning value leaves a
dangling raw pointer. There is no lifetime checking for this borrow.

Enumeration consumes the file's current position. Starting another enumeration
does not reopen or rewind the file. Multiple live iterators share that position
and consume each other's remaining input. `Any`, `Count`, and early termination
also consume lines. A later enumeration continues from the position they left.
`Take(0)` constructs its iterator without advancing the file.

`Current` is a checked `ref immutable(String)` property. Reading metadata or
borrowing its data does not copy the String. Its references, pointers, and spans
are invalidated by the next `MoveNext`, iterator movement, or destruction.
Reading `Current` before the first successful advance or after completion throws.
To keep a line, copy it normally: `String saved = iterator.Current;`.

`foreach (String line in source.Lines())` and `ToVector()` follow ordinary value
semantics and own their copied line buffers. `AsEnumerable()` itself only copies
the pointer view, so it does not materialize lines. Early exit, callback failure,
and iterator destruction clean up the iterator's current buffer without closing
the File.

I/O and allocation errors propagate. After a failed advance `Current` is invalid,
and some input may already have been consumed, including only a prefix of a line.
No rollback or line recovery is promised. Leave the iterator's scope to release
any partial buffer. The caller still owns and controls the File.
