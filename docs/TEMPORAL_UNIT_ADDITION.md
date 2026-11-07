# Adding date and time units

`DateTime` and `DateTimeOffset` provide `AddTicks(long)` on the core types.
Load `Standard/core/Time/temporal_add_units.gf` for `AddDays`, `AddHours`,
`AddMinutes`, `AddSeconds`, `AddMilliseconds`, and `AddMicroseconds`.
Each accepts either an exact `long` count or a fractional `double` count.
The same six names are available as freestanding functions.

```csharp
using Standard::Time;

int main()
{
    DateTime start = new(new Date(2024, 2, 29), new TimeOfDay(12, 0));
    DateTime later = start.AddDays(1).AddHours(0.5).AddTicks(7l);
    if (later.Month != 3 || later.Day != 1 || later.Minute != 30) return 1;
    if (later.Nanosecond != 700) return 2;
    DateTimeOffset stamp = new(start, TimeSpan::FromHours(2l));
    DateTimeOffset next = AddMinutes(stamp, 1.5);
    if (next.Minute != 1 || next.Second != 30) return 3;
    if (next.Offset.Ticks != stamp.Offset.Ticks) return 4;
    return 0;
}
```

Fractional additions convert the whole and fractional units separately to
100-nanosecond ticks. Any fraction smaller than one tick is truncated toward
zero. Use the `long` overload for exact whole counts, including counts that a
`double` cannot represent precisely.

Nonfinite amounts and out-of-range results throw. Fractional amounts also
must not exceed the maximum calendar tick count divided by the unit's tick
scale, rounded down to whole units before conversion to `double`.
`DateTimeOffset` validates both the resulting wall clock and UTC clock and
preserves its offset. These operations do not apply daylight-saving changes.
A failed addition leaves the original value and an assignment destination
unchanged. Successful additions do not allocate.
