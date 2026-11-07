# Fractional time intervals

`TimeSpan::FromMicroseconds`, `FromMilliseconds`, `FromSeconds`, `FromMinutes`,
`FromHours`, and `FromDays` accept `double` values as well as exact whole-unit
`long` values.

The double overloads multiply by the number of 100-nanosecond ticks per unit,
then truncate fractional ticks toward zero. They reject NaN, infinity, and
results outside the tick range. NaN throws
`Exception("TimeSpan value must not be NaN")`. Overflow throws
`Exception("TimeSpan factory overflow")`.

```csharp
using Standard::Time;

int main()
{
    TimeSpan halfSecond = TimeSpan::FromSeconds(0.5);
    TimeSpan precise = TimeSpan::FromMicroseconds(1.25);
    TimeSpan negative = TimeSpan::FromMicroseconds(-1.25);
    if (halfSecond.Ticks != 5000000l || precise.Ticks != 12l || negative.Ticks != -12l) return 1;
    if (precise.Microseconds != 1 || precise.Nanoseconds != 200) return 2;
    if (negative.Microseconds != -1 || negative.Nanoseconds != -200) return 3;
    if (precise.TotalMicroseconds != 1.2 || precise.TotalNanoseconds != 1200.0) return 4;
    return 0;
}
```

## Components and totals

| Property | Meaning |
| --- | --- |
| `Microseconds` | Signed microsecond component within a millisecond, from -999 through 999 |
| `Nanoseconds` | Signed nanosecond component within a microsecond, in multiples of 100 from -900 through 900 |
| `TotalMicroseconds` | Complete interval in microseconds as a double |
| `TotalNanoseconds` | Complete interval in nanoseconds as a double |

Components truncate toward zero and retain the duration's sign, like the
existing `Milliseconds` component. These properties are `nothrow`.

## Precision and limits

Double multiplication can round before the conversion to ticks. Very small
values become zero, and large values can lose tick precision. Both signed
floating-point zeros produce zero ticks. Use `FromTicks(long)` or whole-unit
long overloads for exact construction.

At the upper limit, converting the maximum tick count to double rounds it to
2^63. A scaled value equal to that floating-point endpoint produces the maximum
tick count, matching C#'s duration factories. The negative endpoint produces the
minimum tick count. Values beyond either endpoint throw before integer
conversion.

Whole-microsecond construction uses checked scaling by ten. It throws when the
exact result cannot fit. The stored resolution remains 100 nanoseconds.
