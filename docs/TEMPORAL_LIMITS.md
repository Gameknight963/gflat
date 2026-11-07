# Time value limits

`Date`, `TimeOfDay`, `DateTime`, `DateTimeOffset`, and `TimeSpan` expose
`MinValue` and `MaxValue`. `TimeSpan::Zero` provides a zero duration.

```csharp
using Standard::Time;

int main()
{
    Date last = Date::MaxValue;
    if (last.Year != 9999 || last.Month != 12 || last.Day != 31) return 1;
    TimeOfDay midnight = TimeOfDay::MinValue;
    TimeSpan zero = TimeSpan::Zero;
    if (midnight.Ticks != 0l || zero.Ticks != 0l) return 2;
    DateTimeOffset maximum = DateTimeOffset::MaxValue;
    if (maximum.Offset.Ticks != 0l || maximum.Year != 9999) return 3;
    return 0;
}
```

| Type | Minimum | Maximum |
| --- | --- | --- |
| `Date` | January 1, year 1 | December 31, year 9999 |
| `TimeOfDay` | Midnight | One 100-nanosecond tick before midnight |
| `DateTime` | First supported date at midnight | Last supported date at its final tick |
| `DateTimeOffset` | Minimum `DateTime` at zero UTC offset | Maximum `DateTime` at zero UTC offset |
| `TimeSpan` | -9,223,372,036,854,775,808 ticks | 9,223,372,036,854,775,807 ticks |

Each property returns an independent ordinary value, is `nothrow`, and allocates
no storage. These endpoints do not query a clock or select a local time zone.
`DateTime` remains a civil wall time without an implied offset or instant.

Existing arithmetic rules apply. Date and date-time operations throw when a
result leaves the supported range. `TimeOfDay` addition wraps within a day.
Negating `TimeSpan::MinValue` throws because its positive magnitude is outside
the duration range. Changing the offset of a `DateTimeOffset` endpoint can throw
if the resulting wall time would be out of range.

The properties are part of their respective files under `Time/` and need no
additional source file.

## Duration tick units

`TimeSpan` also exposes compile-time `long` constants for converting tick counts:

| Constant | Value |
| --- | ---: |
| `NanosecondsPerTick` | 100 |
| `TicksPerMicrosecond` | 10 |
| `TicksPerMillisecond` | 10,000 |
| `TicksPerSecond` | 10,000,000 |
| `TicksPerMinute` | 600,000,000 |
| `TicksPerHour` | 36,000,000,000 |
| `TicksPerDay` | 864,000,000,000 |

For example, `TimeSpan::FromTicks(TimeSpan::TicksPerDay)` represents one day.
Multiplying arbitrary input counts by these constants can overflow ordinary
integer arithmetic. Use checked factories such as `TimeSpan::FromDays` when
converting a duration count.
