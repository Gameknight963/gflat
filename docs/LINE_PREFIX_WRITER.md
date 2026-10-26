# Prefix streamed lines

`std::LinePrefixWriter` is an `ITextWriter` adapter. It borrows an output writer
and either a counted byte prefix or an immutable IFormattable view. It inserts the prefix when the first input byte of each
LF-delimited line arrives. It preserves every source byte, including NUL, high
bytes, CR, and LF. It neither validates encoding nor normalizes line endings.

For prefix `> `, input `one\n\ntwo\r\n` becomes `> one\n> \n> two\r\n`.
An empty input produces no prefix. A final LF produces no dangling prefix.
A bare CR is ordinary text; CRLF split between writes behaves exactly as one
write. Prefix bytes are literal output and do not affect input line state,
even when the prefix itself contains LF. An empty prefix forwards unchanged
bytes. Zero-length writes read no source or prefix storage and emit no calls.

```csharp
using std; using std::IO;

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

Normal operations allocate no memory internally. Formatters and sinks may allocate
or throw. A sink can fail after emitting part of a prefix or input run. Its
original exception propagates, and all later writes, including empty writes,
reject the failed writer. There is no rollback or retry guarantee.

Copying duplicates line-start and failure state but shares the borrowed sink and
prefix. Copies do not coordinate their line state: do not interleave them to
produce one logical stream. Moving transfers the same metadata. Destruction
does not dispose either borrowed resource.

## Formatted prefixes

The overload `LinePrefixWriter(ITextWriter* destination, immutable(IFormattable)* prefix)`
borrows a formatter object instead of prefix bytes. It calls the object's
`Format(destination)` exactly once when the first source byte of each new line
arrives. An empty line represented by LF still gets a prefix. Empty writes and
a trailing LF do not request an extra prefix. The formatter can emit any number
of writes, including none; its emitted LF bytes never affect input line state.
The counted constructor keeps its existing behavior and does not invoke Format.

```csharp
using std; using std::Time;

struct TimestampPrefix : IFormattable
{
    public DateTime Value;
    public TimestampPrefix(DateTime value) { Value = value; }
    public immutable void Format(ITextWriter* output)
    {
        Value.Format(output);
        output.Write(" ", 1);
    }
}

int main()
{
    char[128] storage;
    BufferWriter output = new(storage, 128);
    TimestampPrefix prefix = new(DateTime::Parse("2026-10-24T12:00:00"));
    LinePrefixWriter lines = new(&output, &prefix);
    lines.Write("first\n", 6);
    prefix.Value = DateTime::Parse("2026-10-24T12:00:01");
    lines.Write("second\n", 7);
    if (output.Length != 53) return 1;
    return 0;
}
```

This fixed-date example has no OS clock dependency. A caller can instead keep
its own formatter that obtains UTC time from an explicitly referenced platform
clock library; LinePrefixWriter itself never chooses or queries a clock.
The prefix object must outlive every write using it, and it is not copied or
owned. The immutable interface view permits calling its immutable Format method;
it does not freeze other aliases. Changes between calls are visible at later
line starts. Prefix formatters must not mutate themselves or their backing
storage through another alias during Format, retain borrowed output/input
addresses, or reenter/copy/use this writer during a callback.

The writer marks an operation as failed before invoking either the formatter or
the sink. If either throws, its original exception propagates; every later
write, including an empty one, rejects the writer with its existing error.
There is no rollback, automatic retry, or formatter call after failure.
The destination may already contain partial prefix or input bytes. Copies share
the borrowed formatter and destination but keep separate line/failure state,
just as with a counted prefix. Neither resource is disposed by the adapter.
