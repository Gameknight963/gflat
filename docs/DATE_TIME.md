# Date and time

`Standard::Time::DateTime` combines a Gregorian [Date](DATE.md) with a
[TimeOfDay](TIME_OF_DAY.md). It describes a calendar date and clock reading,
without a time zone or UTC offset. It does not identify an instant or consult
the system clock. Two values compare their calendar and clock readings directly.

```csharp
using Standard::Text;
using Standard; using Standard::Time;
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

The default value is `0001-01-01T00:00:00`. Construction from `Date` and
`TimeOfDay` preserves their values. Immutable getters expose `Date`, `TimeOfDay`,
and `Ticks`. Ticks count 100-nanosecond intervals since the default value;
`FromTicks(long)` accepts 0 through 3155378975999999999, covering years 1..9999.
Invalid tick counts throw `Exception`.

`Add(TimeSpan)` and `Subtract(TimeSpan)` return new values and reject results outside that range.
Subtraction accepts signed durations: a negative duration moves the result later,
and zero preserves the value. Neither operation changes its receiver; accepted
arithmetic performs no library allocation.
`Difference(other)` returns this value minus the other as a `TimeSpan`.
`AddMonths(int)` and `AddYears(int)` use Date's calendar rules, including clamping
the day to the destination month's length, and preserve the time of day.
These are calendar operations: no daylight-saving adjustment is performed.
Equality, ordering, and hashing use the complete date and time.

## Text

Counted `TryParse(text, count, &result)` and `Parse(text, count)` accept exactly
`yyyy-MM-ddTHH:mm:ss`, optionally followed by a decimal point and one through
seven fractional-second digits. The separator is an uppercase `T`. Year zero,
invalid dates, leap seconds, whitespace, `Z`, offsets, and trailing bytes are
rejected. Counted embedded NUL bytes are rejected too. C-string overloads stop
at the first NUL and inspect at most 28 bytes. Try failure resets the result to
the default value; throwing Parse reports an `Exception`. Try parsing allocates
nothing and is `nothrow`. Properly backed, aligned result storage may overlap
the input; parsing commits the result after reading the input it needs.

`TryFormat(output, capacity, ref written)` writes 19 bytes when the fraction is
zero, otherwise 27 bytes with exactly seven fractional digits. No NUL terminator
is appended. Insufficient capacity returns false, sets `written` to zero, and
leaves output bytes unchanged. Successful formatting allocates nothing and is
`nothrow`. Output may overlap sufficiently backed receiver storage; `written`
must be separate from the receiver and output bytes.

`Format(writer)` sends the same representation in one counted write from a
stack buffer. Writer errors propagate. Interpolation uses this method through
`IFormattable`; DateTime itself does not depend on String.

The implementation reuses Date and TimeOfDay for calendar validation, parsing,
and formatting. It provides no current-time lookup, Unix timestamp conversion,
time-zone rules, or localized formatting.
