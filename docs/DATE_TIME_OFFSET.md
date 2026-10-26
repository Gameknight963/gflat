# Civil timestamps with an explicit UTC offset

`std::Time::DateTimeOffset` pairs a civil DateTime with a fixed whole-minute TimeSpan
offset from UTC. Construction accepts offsets from `-14:00` through `+14:00`,
inclusive, and requires both wall-clock and UTC values within years 1 through 9999.
The default value is 0001-01-01 at midnight UTC. Values contain no named time zone,
daylight-saving rules, local-machine settings or clock/provider ownership.

```csharp
using std; using std::Time;

int main()
{
    DateTime wall = DateTime::Parse("2024-06-01T12:30:00");
    DateTimeOffset first = new(wall, TimeSpan::FromHours(2l));
    DateTimeOffset utc = first.ToOffset(TimeSpan::FromTicks(0l));
    if (!first.Equals(&utc) || first.EqualsExact(&utc)) return 1;
    if (utc.DateTime.TimeOfDay.Hour != 10) return 2;
    DateTimeOffset beforeEpoch = DateTimeOffset::FromUnixTimeMilliseconds(-1l);
    if (beforeEpoch.ToUnixTimeSeconds() != -1l || beforeEpoch.ToUnixTimeMilliseconds() != -1l) return 3;
    return 0;
}
```

`DateTime`, `UtcDateTime` and `Offset` return ordinary independent value copies.
`ToOffset` preserves the instant while changing the wall time/offset, or throws
if its new offset or wall time is out of range. Construction and ToOffset throw
`Exception("DateTimeOffset clock or offset is out of range")`. Offset durations
with fractional minutes, even one tick, are rejected. Equality, ordering and hash
use UTC ticks; `EqualsExact` also requires the same wall time and offset.

`FromUnixTimeSeconds(seconds)` and `FromUnixTimeMilliseconds(milliseconds)`
construct an offset-zero UTC value. Seconds must be in
`-62135596800..253402300799`; milliseconds must be in
`-62135596800000..253402300799999`. Outside these inclusive ranges they throw
`Exception` before epoch shifting or multiplication. They do not consult a clock
or local time-zone settings.

`ToUnixTimeSeconds()` and `ToUnixTimeMilliseconds()` are immutable, `nothrow`
observers of the UTC instant, independent of the displayed offset. They round
down to the containing second or millisecond, including before the 1970 epoch:
`1969-12-31T23:59:59.9999999Z` yields `-1` in both units. Factories preserve only
their input unit's precision; converting a fractional timestamp to a Unix integer
and back discards the remaining fraction. Normal valid conversions allocate
nothing internally; invalid factory calls use ordinary exception behavior.

## Duration arithmetic

`Add(TimeSpan)` and `Subtract(TimeSpan)` return a new timestamp with the same
displayed offset and the duration added to or removed from both its wall clock
and UTC instant. Both results must remain within years 1 through 9999. The methods
check before arithmetic, including for `TimeSpan` values containing signed-long
minimum or maximum ticks. Out-of-range results throw `Exception` with
`DateTimeOffset addition is outside years 1 through 9999` or
`DateTimeOffset subtraction is outside years 1 through 9999`.

`Difference(DateTimeOffset other)` returns a `TimeSpan` measuring the
UTC instant difference, independently of either displayed offset. It is immutable
and `nothrow`; even the full supported date range fits a signed-long tick count.
The operand is an ordinary value copy, matching DateTime's API. Self-difference is zero.

These are exact elapsed-duration operations, with no calendar month/year,
daylight-saving or named-zone adjustment. They do not mutate either operand or
allocate internally for successful calls. A failed result assignment preserves
its destination; throwing errors use normal exception storage. No operators or
floating-point unit conversions are added.

```csharp
using std; using std::Time;

int main()
{
    DateTimeOffset start = DateTimeOffset::Parse("2024-06-01T12:30:00+02:00");
    DateTimeOffset end = start.Add(TimeSpan::FromMinutes(15l));
    TimeSpan elapsed = end.Difference(start);
    DateTimeOffset restored = end.Subtract(elapsed);
    if (elapsed.Ticks != 9000000000l || !restored.EqualsExact(&start)) return 1;
    return 0;
}
```

## Parsing and formatting

Accepted text is exactly `yyyy-MM-ddTHH:mm:ss[.fraction]Z` or the same civil prefix
followed by `+HH:mm`/`-HH:mm`. Fractions have 1 through 7 decimal digits. `T`/`Z` are
uppercase. No whitespace, absent suffix, named zone, offset seconds, leap seconds,
or trailing bytes are accepted. Numeric `-00:00` and `+00:00` both denote zero
offset and format as `Z`. The civil prefix follows DateTime's exact Gregorian
grammar; range checks include the resulting UTC instant.

`TryParse(text, count, DateTimeOffset*)` and the CString overload are `nothrow` and
allocate nothing internally. Failure resets the output to the default value.
Counted input includes embedded NUL, which is invalid; CString input stops at its
terminator and scans at most 34 bytes. Counts below 20 or above 33 reject before input
access. `Parse` overloads throw `Exception("Invalid date and time offset")` for
invalid text. There is no implicit local offset or offset lookup.

Canonical formatting uses DateTime's 19-byte civil form, or 27 bytes with a nonzero
seven-digit fraction, then `Z` for zero offset or six bytes for a numeric offset.
`TryFormat(output, capacity, ref written)` adds no terminator: insufficient capacity
leaves output untouched and sets written to zero. `Format` sends one counted call
to its borrowed writer, without flushing. Writer exceptions propagate and can
leave a prefix; the value remains unchanged.

Input/output may overlap fully backed, aligned receiver/result storage: parsing
and formatting stage their data before committing. `written` must be separate
from receiver and output. Normal valid operations allocate nothing internally;
throwing errors or a destination writer can allocate. `UtcDateTime` uses the
checked DateTime factory internally and has an ordinary getter. Other primitive
observers, equality and formatting are declared `nothrow` where shown in source.

Include `Time/DateTimeOffset.gf` and the existing DateTime/TimeSpan dependencies.
No OS, String, libc or time-zone database dependency is added by this source.
