# Date and clock components

`DateTime` and `DateTimeOffset` expose these direct `nothrow` properties:

| Properties | Meaning |
| --- | --- |
| `Year`, `Month`, `Day`, `DayOfYear`, `DayOfWeek` | Gregorian calendar components |
| `Hour`, `Minute`, `Second`, `Millisecond` | Clock components |
| `Microsecond` | Microseconds within the millisecond, from 0 through 999 |
| `Nanosecond` | Nanoseconds within the microsecond, in multiples of 100 from 0 through 900 |

`DateTimeOffset` properties describe its stored wall clock. Its `Date` and
`TimeOfDay` properties return the separate date and clock values. `Ticks`
counts wall-clock ticks since 0001-01-01, and `UtcTicks` counts ticks after
applying the stored offset. Use `UtcDateTime` for UTC calendar components.

```csharp
using Standard::Time;

int main()
{
    TimeOfDay precise = new(1, 2, 3, 4, 5);
    TimeOfDay exact = TimeOfDay::FromTicks(precise.Ticks + 7l);
    DateTime clock = new(new Date(2024, 2, 29), exact);
    DateTimeOffset stamp = new(clock, TimeSpan::FromHours(2l));
    if (clock.Year != 2024 || clock.DayOfYear != 60 || clock.Microsecond != 5 || clock.Nanosecond != 700) return 1;
    if (stamp.Hour != 1 || stamp.UtcDateTime.Hour != 23 || stamp.UtcDateTime.Day != 28) return 2;
    if (stamp.UtcTicks != stamp.Ticks - 72000000000l) return 3;

    TimeSpan sinceMidnight = exact.ToTimeSpan();
    if (TimeOfDay::FromTimeSpan(sinceMidnight).Ticks != exact.Ticks) return 4;
    TimeSpan elapsed = new TimeOfDay(1, 0).Subtract(new TimeOfDay(23, 0));
    if (elapsed.Ticks != TimeSpan::FromHours(2l).Ticks) return 5;
    return 0;
}
```

## Clock precision and durations

`TimeOfDay` has the same `Microsecond` and `Nanosecond` properties. Its
constructor accepts an optional fifth `microsecond` argument from 0 through
999, in addition to millisecond precision. Omitted components are zero.
Invalid components throw before a new value is returned. Use `FromTicks` for
the remaining 100-nanosecond fraction.

`ToTimeSpan()` returns the elapsed interval since midnight and is `nothrow`.
`FromTimeSpan(value)` accepts tick counts from zero through one tick before
24 hours. Negative durations and values of 24 hours or more throw. Conversion
does not wrap or truncate a duration.

`value.Subtract(other)` returns the forward elapsed interval from `other` to
`value` on a circular 24-hour clock. Its result is from zero through one tick
before 24 hours, and equal clocks produce zero. It is `nothrow`. A clock
value has no date, so this operation cannot count elapsed whole days.

Successful component access and clock/duration conversion do not allocate.
These APIs are part of the corresponding core time types, with no additional
extension source required.
