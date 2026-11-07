# Constructing durations from components

`TimeSpan` supports these constructors in its core source file:

- `TimeSpan(long ticks)` for an exact signed 100-nanosecond count, `nothrow`
- `TimeSpan(int hours, int minutes, int seconds)`
- `TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds = 0, int microseconds = 0)`

```csharp
using Standard::Time;

int main()
{
    TimeSpan work = new(1, 30, 0);
    if (work.Ticks != TimeSpan::FromMinutes(90l).Ticks) return 1;
    TimeSpan mixed = new(1, -24, 0, 0, 0, 1);
    if (mixed.Ticks != 10l) return 2;
    TimeSpan exact = new(-1l);
    if (exact.Nanoseconds != -100) return 3;
    return 0;
}
```

Components are signed quantities to add, rather than individually bounded clock
fields. For example, 90 minutes is valid and one day plus -24 hours is zero.
Milliseconds and microseconds can also be negative or greater than 999.
The calculation uses a wide intermediate and checks only the combined tick
count. A duration outside the signed 64-bit tick range throws before an
assignment destination is replaced. Successful construction does not allocate.

Component construction has microsecond precision. Use the ticks constructor
or `FromTicks` to include the remaining 100-nanosecond fraction. The existing
`FromDays` through `FromMicroseconds` factories remain useful for a single unit.
