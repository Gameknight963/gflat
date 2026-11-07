# Parse and format time values through spans

`Date`, `TimeOfDay`, `TimeSpan`, `DateTime`, and `DateTimeOffset` accept
`Span<readonly(char)>` in `Parse` and `TryParse`. Their `TryFormat` methods
also accept `Span<char>` destinations.

```csharp
using Standard::Memory;
using Standard::Time;

int main()
{
    readonly(char)* input = "[2026-11-07]";
    Date date = Date::Parse(new Span<readonly(char)>(input + 1, 10));
    char[10] output;
    nuint written = 0;
    if (!date.TryFormat(new Span<char>(&output[0], 10), ref written)) return 1;
    if (written != 10 || date.Year != 2026) return 2;
    return 0;
}
```

The complete input span must match the type's existing invariant text format.
No null terminator is required. Embedded null bytes are input, not terminators.
Empty or invalid input makes `TryParse` return false and reset the output to
the type's default value. `Parse` throws on invalid input.

Parsing valid input, `TryParse`, and `TryFormat` allocate nothing. `TryParse`
and `TryFormat` are `nothrow`. Insufficient destination capacity makes
`TryFormat` return false, set `written` to zero, and leave the bytes unchanged.
Successful formatting writes counted bytes without a terminator. Empty spans
never access their data pointer.

The pointer overloads' aliasing rules still apply. In particular, `written`
must be separate from the destination and receiver. For formats and supported
value ranges, see [dates](DATE.md), [time intervals](TIME_SPAN.md),
[time of day](TIME_OF_DAY.md), [civil date and time](DATE_TIME.md), and
[offset date and time](DATE_TIME_OFFSET.md).

Selective source builds of these time types now also need `Memory/Span.gf`,
`Queries/Enumerable.gf`, and `Collections/IEnumerable.gf`. They still need no
owning string or libc implementation.
