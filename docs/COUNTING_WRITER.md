# Measure writer output

`Standard::Text::CountingWriter` implements `ITextWriter` by accumulating byte
counts. It ignores the data pointer, including on nonempty writes.

## Validate and measure a decoded file

```csharp
using Standard::Text;
using Standard;
using Standard::IO;
using Standard::Encoding;

int main()
{
    File input = File::OpenRead("input.txt");
    CountingWriter count = new();
    Base64Decoder decoder = new(&count);
    char[4096] buffer;
    input.CopyTo(&decoder, buffer, 4096);
    decoder.Complete();
    input.Close();
    count.Length.Format(Console::Out);
    Console::WriteLine();
    Console::Flush();
    return 0;
}
```

The example reports the decoded byte count after Base64 completion succeeds.
A decoding failure can leave a count for an accepted prefix.

## Counter operations

| Operation | Effect |
| --- | --- |
| Construction | Starts at zero |
| `Length` | Current count |
| `TryWrite(data, count)` | Adds the count, or returns false on `nuint` overflow |
| `Write(data, count)` | Adds the count, or throws `Exception("Writer length overflow")` |
| `Clear()` | Resets the count to zero |

Overflow preserves the previous count. The writer can still accept later fitting
writes. Copies have independent counters.

## Measuring formatting

Pass a counter to any formatter accepting `ITextWriter*` to measure its output.
If you then format a second time into reserved storage, keep the input and
formatter behavior stable between passes. A formatter observing time or changing
state may produce a different length.

Source: `core/Text/CountingWriter.gf`
