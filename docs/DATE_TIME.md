# Date and time

`Standard::Time::DateTime` combines a [Date](DATE.md) and [TimeOfDay](TIME_OF_DAY.md).
It describes a calendar date and clock reading. Use [DateTimeOffset](DATE_TIME_OFFSET.md)
when the value must identify a UTC instant.

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

int main()
{
    DateTime appointment = new(new Date(2026, 10, 24), new TimeOfDay(23, 30));
    DateTime next = appointment.Add(TimeSpan::FromHours(2));
    if (next.Date.Day != 25 || next.TimeOfDay.Hour != 1) return 1;
    if (next.Difference(appointment).Ticks != TimeSpan::FromHours(2).Ticks) return 2;
    DateTime parsed = DateTime::Parse("2026-10-25T01:30:00");
    if (!next.Equals(&parsed)) return 3;
    String text = s$"{next}";
    if (!text.Equals("2026-10-25T01:30:00")) return 4;
    return 0;
}
```

## Values and arithmetic

Default construction gives `0001-01-01T00:00:00`. Construct from Date and
TimeOfDay, or use `FromTicks(long)`. Ticks count 100-nanosecond intervals since
the default value, from zero through `3155378975999999999`.

`Add(TimeSpan)` and `Subtract(TimeSpan)` return shifted values and reject
out-of-range results. `Difference(other)` returns this value minus the other.
`AddMonths` and `AddYears` use Date's calendar clamping rules and preserve the
time of day. These operations compare and adjust the supplied clock readings
directly, without timezone or daylight-saving adjustments.

## Text

Parse and TryParse accept `yyyy-MM-ddTHH:mm:ss`, optionally followed by a
decimal point and one through seven fractional digits. An uppercase T is
required. Invalid dates, leap seconds, whitespace, Z, offsets, and trailing
bytes are rejected. TryParse resets the result to the default on failure.
The C-string overload examines at most 28 bytes.

TryFormat writes 19 bytes for whole seconds or 27 with a nonzero seven-digit
fraction. Output is counted. Insufficient capacity returns false, writes zero
to `written`, and preserves the buffer. Format and interpolation use the same
representation.

TryParse and TryFormat are `nothrow`. Parsing and formatting stage their data,
so properly backed result or receiver storage can overlap the byte input or
output. Keep `written` separate from receiver and output.

Source: `Time/DateTime.gf`, with Date and TimeOfDay dependencies.
