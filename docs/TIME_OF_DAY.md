# Time of day

`std::Time::TimeOfDay` stores a time within one day as a private `long` tick count.
One tick is 100 nanoseconds. Valid values range from midnight through
`23:59:59.9999999`; default construction produces midnight.

```csharp
using std; using std::Time;
int main()
{
    TimeOfDay opening = new(9, 30);
    TimeOfDay closing = TimeOfDay::Parse("17:45:00.125");
    if (opening.CompareTo(&closing) >= 0) return 1;
    String label = s$"{opening} - {closing}";
    if (!label.Equals("09:30:00 - 17:45:00.1250000")) return 2;
    int days = 0;
    TimeOfDay next = closing.Add(TimeSpan::FromHours(8), ref days);
    if (days != 1 || next.Hour != 1 || next.Minute != 45) return 3;
    if (!next.IsBetween(new TimeOfDay(23, 0), new TimeOfDay(2, 0))) return 4;
    return 0;
}
```

The component constructor accepts `hour`, `minute`, optional `second = 0`, and
optional `millisecond = 0`. Hours must be 0..23, minutes and seconds 0..59,
and milliseconds 0..999. `FromTicks(long)` accepts 0..863999999999 for finer
precision. Invalid components or tick counts throw `Exception`.

Immutable, `nothrow` properties expose `Ticks`, `Hour`, `Minute`, `Second`, and
`Millisecond`. The value implements equality, ordering, hashing, and direct
writer formatting. Ordering compares times within the same day; no date or
time zone is attached. Use [TimeSpan](TIME_SPAN.md) for signed durations.

## Arithmetic and ranges

Immutable `Add(TimeSpan value)` returns the time after a positive or negative
duration, wrapping within one day. `Add(value, ref int wrappedDays)` also reports
the signed day crossings. For example, adding minus one tick to midnight returns
`23:59:59.9999999` with `wrappedDays = -1`; the last tick plus one tick returns
midnight with `wrappedDays = 1`. Whole days contribute to this count even when
the time itself is unchanged. Every signed-long TimeSpan tick count is accepted,
including its minimum and maximum. Both overloads are `nothrow` and allocate
nothing; no date, time zone, daylight-saving or clock operation is implied.

Immutable, `nothrow` `IsBetween(start, end)` includes `start` and excludes `end`.
When start is later than end, the interval crosses midnight. Equal endpoints
describe an empty interval and return false. The operation compares times only
and does not infer dates or a full-day interval from equality.

These contracts follow [TimeOnly.Add](https://learn.microsoft.com/en-us/dotnet/api/system.timeonly.add)
and [TimeOnly.IsBetween](https://learn.microsoft.com/en-us/dotnet/api/system.timeonly.isbetween).

## Text

`TryParse(text, count, &result)` consumes complete counted ASCII input in the
form `HH:mm:ss[.fraction]`. Hours, minutes, and seconds require two digits; an
optional fraction contains one through seven digits. Signs, day prefixes,
whitespace, `24:00:00`, and leap-second spellings are rejected. Failure returns
false and resets the result to midnight. The C-string overload stops at its
first NUL and reads at most 17 bytes. Throwing `Parse` overloads use the same rules.

`TryFormat(output, capacity, ref written)` emits `HH:mm:ss`, adding exactly seven
fractional digits when the fraction is nonzero. It writes counted bytes without
a terminator and needs at most 16 bytes. Insufficient capacity returns false,
sets `written` to zero, and leaves output bytes unchanged. `Format(writer)`
and interpolation use this same representation.

Successful construction, parsing, comparison, and caller-buffer formatting allocate nothing.
`TryParse` and `TryFormat` are `nothrow`; a writer can allocate or throw normally.
Parsing reads input before writing the result, so an aligned result may overlap
input. Formatting stages its bytes before writing output, allowing output to
overlap the receiver when enough writable storage backs that region. The
`written` variable must be separate from output and receiver storage.

The implementation uses the existing pure `TimeSpan` codec with a restricted
time-only grammar and range. For selective source builds, include `Time/TimeOfDay.gf`,
`Time/TimeSpan.gf`, their comparison/hash interfaces, `Hashing/primitive_hashing.gf`, and the normal
formatting and exception contracts. No clock or operating-system API is needed.
