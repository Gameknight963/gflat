# Prefix streamed lines

`Standard::Text::LinePrefixWriter` inserts a prefix before the first byte of
each LF-delimited line. For prefix `> `, input `one\n\ntwo\r\n` becomes
`> one\n> \n> two\r\n`.

```csharp
using Standard::Text;
using Standard;
using Standard::IO;

int main()
{
    FileStream input = File::OpenRead("input.txt");
    FileStream output = File::OpenWrite("prefixed.txt");
    LinePrefixWriter prefixed = new(&output, "> ", 2);
    char[4096] buffer;
    input.CopyTo(&prefixed, buffer, 4096);
    input.Close();
    output.Close();
    return 0;
}
```

LF is the line boundary. CR is ordinary data, including when CRLF spans two
writes. Empty input and a final LF produce no extra prefix. LF bytes inside the
prefix itself do not affect input line boundaries.

The counted prefix is borrowed. Keep it and the destination alive. Changing
prefix bytes between writes changes later prefixes. Input and prefix storage
must be separate from storage the destination may modify.

## Formatted prefixes

`LinePrefixWriter(ITextWriter* destination, immutable(IFormattable)* prefix)`
calls `prefix.Format(destination)` once per input line, as its first byte arrives.
An empty line gets a prefix too.

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

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

Keep the formatter alive. Changes between writes are visible at later line
starts, as the example demonstrates with timestamps. To generate current time,
the formatter can query a chosen platform clock.

## Errors and copies

A formatter or destination exception can leave a partial prefix or line and
permanently fails the adapter. Later writes throw. Callbacks must not reenter
this writer.

Copies share the destination and prefix but duplicate line/failure state.
Use one adapter per logical stream so copies do not insert competing prefixes.

Source: `core/Text/LinePrefixWriter.gf`
