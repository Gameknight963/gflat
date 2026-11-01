# Borrowed file lines

`FileStream.Lines()` returns a deferred FileLineSequence:

```csharp
using Standard::Text;
using Standard;
using Standard::IO;
using Standard::Queries;
using Standard::Collections;

int main()
{
    FileStream input = File::OpenRead("input.txt");
    FileStream* source = &input;
    Vector<String> lines = source.Lines().AsEnumerable().ToVector();
    return (int)lines.Count;
}
```

Each iterator reuses one String. LF and CRLF end a line and are removed. Bare
CR and embedded NUL remain contents. Empty terminated lines and a final
nonempty unterminated line are yielded.

## Ownership and file position

Keep the FileStream alive, open, and at the same address while using the sequence.
All enumerators share its cursor. A new enumeration continues from the current
position. Query Any, Count, and Take consume input too.

`ref immutable(String) Current` borrows the reusable line until the next advance,
iterator move, or destruction. Copy it into a String to retain a line.
`foreach (String line in lines)` and ToVector create owning copies.

Read or allocation failure invalidates Current and may leave input consumed.
Dispose that iterator after handling the partial read. For arbitrary IByteReader
sources, use [EnumerateLines](BYTE_READERS.md#enumerate-lines-without-owning-the-source).

Source: `libc/IO/FileLineSequence.gf`
