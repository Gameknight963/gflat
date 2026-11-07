# Adding units to a clock value

`TimeOfDay` has `AddTicks(long)`, `AddHours(long/double)`, and
`AddMinutes(long/double)`. Each also accepts `ref int wrappedDays` as its
last argument. The methods belong to the core clock type.

```csharp
using Standard::Time;

int main()
{
    TimeOfDay late = new(23, 45);
    int days = 0;
    TimeOfDay next = late.AddHours(1.5, ref days);
    if (next.Hour != 1 || next.Minute != 15 || days != 1) return 1;
    TimeOfDay previous = new TimeOfDay(0, 15).AddMinutes(-30l, ref days);
    if (previous.Hour != 23 || previous.Minute != 45 || days != -1) return 2;
    return 0;
}
```

Results wrap within a 24-hour clock. `wrappedDays` reports the signed number
of crossed midnights, including whole days in the input. The source is not
modified. Omitting that output returns the same clock value.

Ticks are exact and both tick overloads are `nothrow`. Whole hours and minutes
use the checked integer `TimeSpan` factories. Fractional values use the matching
fractional factory, multiplying into 100-nanosecond ticks and truncating any
remaining fraction toward zero. Their supported range and endpoint behavior
are described in [Fractional time intervals](TIME_SPAN_FRACTIONAL.md).

Nonfinite amounts and amounts that cannot be converted to a supported duration
throw. A conversion failure leaves the clock assignment destination and
`wrappedDays` unchanged. Successful additions do not allocate. These are
clock operations without a date, time zone, or daylight-saving adjustment.
