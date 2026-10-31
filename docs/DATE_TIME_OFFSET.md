# Timestamps with a UTC offset

`Standard::Time::DateTimeOffset` pairs a wall-clock DateTime with a fixed UTC
offset. Both wall time and UTC must fit years 1 through 9999. The offset is a
whole number of minutes between -14:00 and +14:00.

```csharp
using Standard;
using Standard::Time;

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

`DateTime`, `UtcDateTime`, and `Offset` expose the components. `ToOffset` keeps
the instant while changing its displayed wall time. Equality, ordering, and
hashing use UTC ticks. `EqualsExact` also requires the same wall time and
offset.

## Duration arithmetic

Add and Subtract preserve the displayed offset and shift both wall time and
UTC by the duration. Either leaving the supported range throws. Difference
returns the elapsed TimeSpan between UTC instants.

```csharp
using Standard;
using Standard::Time;

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

## Calendar arithmetic

AddMonths and AddYears preserve the time and offset, clamping the day to the
destination month's length. Chained calls use that clamped day. Wall and UTC
results must both remain in range. These fixed-offset calculations do not
apply named-zone daylight-saving rules.

```csharp
using Standard;
using Standard::Time;

int main()
{
    DateTimeOffset start = DateTimeOffset::Parse("2024-01-31T12:34:56.1234567+05:45");
    DateTimeOffset february = start.AddMonths(1);
    DateTimeOffset nextYear = february.AddYears(1);
    DateTimeOffset expected = DateTimeOffset::Parse("2025-02-28T12:34:56.1234567+05:45");
    if (february.DateTime.Date.Day != 29 || !nextYear.EqualsExact(&expected)) return 1;
    if (start.DateTime.Date.Day != 31) return 2;
    return 0;
}
```

## Unix time

Factories create UTC values with offset zero:

| Factory | Accepted range |
| --- | --- |
| `FromUnixTimeSeconds` | `-62135596800..253402300799` |
| `FromUnixTimeMilliseconds` | `-62135596800000..253402300799999` |

ToUnixTimeSeconds and ToUnixTimeMilliseconds round down to the containing
unit. For example, `1969-12-31T23:59:59.9999999Z` yields `-1` in either unit.
Converting back loses the discarded fractional part.

## Parsing and formatting

Text uses `yyyy-MM-ddTHH:mm:ss[.fraction]` followed by `Z` or `+HH:mm`/`-HH:mm`.
T and Z are uppercase, and fractions contain one through seven digits.
Whitespace, missing offsets, leap seconds, and trailing bytes are invalid.
Numeric `-00:00` and `+00:00` both format as Z.

TryParse resets its output to the default midnight UTC value on failure.
Counted input must contain 20 through 33 bytes. The C-string form examines at
most 34. Parse throws `Exception("Invalid date and time offset")` for invalid
text.

TryFormat writes 19 civil-time bytes, or 27 with a nonzero fraction, followed
by Z or a six-byte numeric offset. Insufficient capacity returns false, resets
`written` to zero, and preserves output. Format sends the same representation
in one writer call.

TryParse and TryFormat are `nothrow`. Input or output can overlap sufficiently
backed result or receiver storage. Keep `written` separate from output and
receiver.

Source: `Time/DateTimeOffset.gf`, with DateTime and TimeSpan dependencies.
