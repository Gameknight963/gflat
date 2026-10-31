# Normalize line endings

`String.ReplaceLineEndings()` replaces each CRLF pair, bare CR, and LF with LF.
Supply a replacement to choose another ending or marker:

```csharp
using Standard::Text;

int main()
{
    String text = s"first\r\nsecond\rthird\n";
    text.ReplaceLineEndings();
    if (!text.Equals("first\nsecond\nthird\n")) return 1;
    text.ReplaceLineEndings("\r\n");
    if (!text.Equals("first\r\nsecond\r\nthird\r\n")) return 2;
    return 0;
}
```

## Replacements

- `ReplaceLineEndings()` uses LF
- `ReplaceLineEndings(replacement)` reads a C string through its first zero
- `ReplaceLineEndings(replacement, count)` copies exactly those bytes, including zeros
- An empty replacement removes line endings

A CR followed immediately by LF is one ending. A CR before another CR is an
ending on its own. Every other byte is preserved, including embedded zeros,
high bytes, and UTF-8 sequences. Replacement bytes are copied as supplied, even
when they contain CR or LF.

## Storage and failures

The operation counts original endings, checks the result length, and builds a new
String before replacing the receiver. The replacement can borrow the original
String's storage, including live bytes retained beyond its current length.

If the source contains an ending, success replaces its storage and invalidates
old views. A nonempty result uses one allocation. An empty result releases the
old allocation. Length overflow or allocation failure leaves the receiver's
contents, length, capacity, and storage unchanged.

A source with no endings keeps its storage and capacity without allocating.
The counted replacement must provide its declared readable bytes when used.
The C-string overload requires a readable terminator.
