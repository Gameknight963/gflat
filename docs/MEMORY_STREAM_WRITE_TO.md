# Writing an entire memory stream

`MemoryStream.WriteTo(ITextWriter* output)` writes every stored byte, beginning
at byte zero, regardless of the source's current position. The source position,
length, and capacity stay unchanged. It is an `immutable` method.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    MemoryStream source = new();
    source.Write("hello", 5);
    source.Position = 3l;
    String result = new();
    source.WriteTo(&result);
    if (!result.Equals("hello") || source.Position != 3l) return 1;
    return 0;
}
```

Use `IByteReader.CopyTo` when copying only the unread suffix and advancing the
source cursor. `WriteTo` instead calls the writer once with the complete counted
content, including embedded zeros. Empty streams still call `Write` with zero
bytes, allowing a writer to validate its state.

The source and destination must be independent. The writer must not mutate or
destroy the source or retain its borrowed buffer after the call. Destination
position and overwrite behavior belong to the writer. A writer failure can
leave partial output, but does not advance the source. The method does not
allocate a copy, flush the destination, or close either endpoint.
