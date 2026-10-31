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

## Parameters and result

| Parameter | Meaning |
| --- | --- |
| `input` | Source, read from its current position and left open |
| `text` | Reusable destination, cleared before the first read |
| `maximumBytes` | Largest accepted input length, defaulting to the largest `nuint` |

`ReadToEnd(input, maximumBytes)` returns a new owning String.
`ReadToEnd(input, ref text, maximumBytes)` reuses the destination's capacity.
Both preserve the input bytes, including embedded zeros and line endings.

## Limits and failures

An exact-length input succeeds. A longer input throws `IOException` after receiving
one byte beyond the limit. A zero limit probes for EOF with a one-byte read.
Short positive reads continue until the source reports EOF.

Source errors propagate. With the ref overload, completed chunks remain in `text`
after failure. A source or allocation error can occur after further input has
already been consumed.

## Borrowing

Buffered sources can read ahead. Continue through the same adapter to consume
its buffered tail.

For the ref overload, source callbacks must keep their state and storage independent
of the destination. They must not access `text` through another alias while reading.
