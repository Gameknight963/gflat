# Unix epochs and zero-offset timestamps

`DateTime::UnixEpoch` is the civil reading 1970-01-01 at midnight. Like other
DateTime values, it carries no time-zone or offset metadata.
`DateTimeOffset::UnixEpoch` represents that UTC instant with a zero offset.
Both have 621355968000000000 ticks since the calendar's year-1 origin.

`DateTimeOffset.ToUniversalTime()` returns the same instant with offset zero.
Its displayed clock changes when necessary. The original value remains unchanged.

```csharp
using Standard::Time;

int main()
{
    TimeSpan offset = TimeSpan::FromHours(2l);
    DateTime clock = DateTime::UnixEpoch.Add(offset);
    DateTimeOffset displayed = new(clock.Ticks, offset);
    DateTimeOffset utc = displayed.ToUniversalTime();
    DateTimeOffset epoch = DateTimeOffset::UnixEpoch;
    if (!utc.EqualsExact(&epoch) || !displayed.Equals(&epoch)) return 1;
    if (displayed.Hour != 2 || displayed.Offset.Ticks != offset.Ticks) return 2;
    if (utc.Hour != 0 || utc.Offset.Ticks != 0l || utc.ToUnixTimeSeconds() != 0l) return 3;
    return 0;
}
```

## Construct from clock ticks

`DateTimeOffset(long ticks, TimeSpan offset)` interprets ticks as the displayed
wall-clock reading, measured in 100-nanosecond units since 0001-01-01. The UTC
instant is that reading minus the offset. These are not Unix ticks and are not
already-adjusted UTC ticks.

The wall clock and resulting UTC clock must both fit years 1 through 9999.
Offsets must be whole minutes within -14:00 through +14:00. Invalid ticks or
offsets throw `Exception`, including a valid wall reading whose UTC conversion
would fall outside the supported range. A failed assignment leaves the previous
DateTimeOffset unchanged.

Epoch getters, successful construction, and `ToUniversalTime` do not allocate.
The getters and UTC conversion are `nothrow` for valid values. These operations
use the core calendar types and do not query a system clock or local time zone.

See [offset timestamps](DATE_TIME_OFFSET.md) for general construction and Unix
seconds/milliseconds conversion, and [time limits](TEMPORAL_LIMITS.md) for range
endpoints.
