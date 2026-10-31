# Read the remaining input

`ReadToEnd` collects bytes from an `IByteReader` into an owning `String`.
Import `Standard::IO` for the extension methods.

```csharp
using Standard::Binary;
using Standard::IO;
using Standard::Text;

int main()
{
    byte[4] bytes = [65, 0, 255, 10];
    BinaryBufferReader input = new(bytes, 4);
    String text = input.ReadToEnd(4);
    if (text.Length != 4 || text[1] != '\0' || (byte)text[2] != (byte)255) return 1;

    input = new(bytes, 4);
    input.ReadToEnd(ref text, 4);
    if (text.Length != 4) return 2;
    return 0;
}
```

- `ReadToEnd(input, maximumBytes)` returns a new owning String
- `ReadToEnd(input, ref text, maximumBytes)` clears `text` and reuses its capacity
- Omitting `maximumBytes` uses the largest `nuint` value
- Embedded zeros, line endings, and high bytes are preserved

## Limits and failures

An exact-length input succeeds. A longer input throws `IOException` after receiving
one byte beyond the limit. A zero limit probes for EOF with a one-byte read.
Short positive reads continue until the source reports EOF.

The limit counts input bytes. String spare capacity, its trailing zero, and source
buffering use additional memory. With the ref overload, earlier completed chunks
remain after failure. Input may have consumed further bytes before a source or
allocation error. A reader reporting more bytes than requested raises `IOException`.

## Borrowing

The helper borrows the source until return and leaves it open at its new position.
Buffered sources can read ahead. Keep the source alive and unmoved. When reusing a
String, keep its state and storage independent of the source. Source callbacks
must not access the destination through another alias. Clearing or growing the
destination can invalidate its old views.
