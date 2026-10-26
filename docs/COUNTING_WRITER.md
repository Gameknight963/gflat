# Count writer output without storing it

`std::CountingWriter` implements `ITextWriter` by counting bytes. It has no output
buffer or destination. Its data pointer argument is never read or retained:
only the supplied count matters. `Length` counts bytes, including embedded NUL,
not Unicode characters or C-string length.

## Validate and measure a decoded file

```csharp
using std; using std::IO; using std::Encoding;

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

This validates strict padded Base64 and reports its decoded byte count without
storing the decoded file. The file is read through a reusable buffer, and the
decoder sends bounded output blocks into the counter. Only a successful
`Complete()` establishes that all encoded input was valid. A failed decoding
operation may already have counted a valid prefix; there is no rollback.

## Counter operations

- `new CountingWriter()` starts at zero and is `nothrow`.
- `Length` is an `immutable`, `nothrow` getter.
- `TryWrite(data, count)` is `nothrow`. It adds `count` if the sum fits in `nuint`;
  otherwise it returns false and leaves the previous length unchanged. The
  pointer is ignored even when `count` is nonzero.
- `Write(data, count)` follows the same rule but throws `Exception` with message
  `Writer length overflow` if the sum would overflow. Failure preserves the
  previous length and does not poison the counter.
- `Clear()` resets the length to zero and is `nothrow`.

A zero-length write always succeeds, including when the count is already the
largest `nuint`. Copying duplicates the current number; the copies advance
independently. Moving transfers the same count. There is no completion, flush,
close, destination ownership, or output on scope exit.

## Measuring formatting

Any formatter that accepts an `ITextWriter*` can write into a counter. Primitive
formatting or a custom `IFormattable` then measures the number of bytes it actually
passes to that writer, without allocating a result string or output buffer.

The counter itself does not allocate on successful operations or failed
`TryWrite`. Throwing overflow constructs an ordinary exception and may allocate.
The formatter, source iterator, codec, or other pipeline component can still
allocate, throw, or have effects of its own.

Measuring is not an automatic preallocation guarantee. Calling a formatter again
may produce different bytes or a different length if it observes changing state,
time, or side effects. A caller that measures and then writes must ensure its own
formatter and input remain suitable for two passes, and still handle write
failures and capacity limits.
