# Prefix streamed lines

`std::LinePrefixWriter` is an `ITextWriter` adapter. It borrows an output writer
and a counted prefix. It inserts that prefix when the first input byte of each
LF-delimited line arrives. It preserves every source byte, including NUL, high
bytes, CR, and LF. It neither validates encoding nor normalizes line endings.

For prefix `> `, input `one\n\ntwo\r\n` becomes `> one\n> \n> two\r\n`.
An empty input produces no prefix. A final LF produces no dangling prefix.
A bare CR is ordinary text; CRLF split between writes behaves exactly as one
write. Prefix bytes are literal output and do not affect input line state,
even when the prefix itself contains LF. An empty prefix forwards unchanged
bytes. Zero-length writes read no source or prefix storage and emit no calls.

```csharp
using std;

int main()
{
    File input = File::OpenRead("input.txt");
    File output = File::OpenWrite("prefixed.txt");
    LinePrefixWriter prefixed = new(&output, "> ", 2);
    char[4096] buffer;
    input.CopyTo(&prefixed, buffer, 4096);
    input.Close();
    output.Close();
    return 0;
}
```

This preserves exact bytes with binary File sinks. Console streams may apply
host text-mode transformations. The writer has no completion, flush, close,
reset, or destructor output. The sink and prefix must remain alive and valid;
the prefix is not copied. Changing prefix bytes between calls changes later
prefixes. Do not mutate prefix storage during a call. Source and prefix storage
must not overlap storage that the sink might modify, move, or reallocate while
writing, and a sink callback must not reenter or copy/use this same writer.

Normal operations allocate no memory internally. Sources and sinks may allocate
or throw. A sink can fail after emitting part of a prefix or input run. Its
original exception propagates, and all later writes, including empty writes,
reject the failed writer. There is no rollback or retry guarantee.

Copying duplicates line-start and failure state but shares the borrowed sink and
prefix. Copies do not coordinate their line state: do not interleave them to
produce one logical stream. Moving transfers the same metadata. Destruction
does not dispose either borrowed resource.
