# Scaling time intervals

`TimeSpan.Duration()` returns the nonnegative interval with the same magnitude.
The minimum tick count has no positive counterpart and throws
`Exception("TimeSpan duration overflow")`.

The scaling extensions in `Standard::Time` provide these operations:

| Method | Result |
| --- | --- |
| `value.Multiply(factor)` | A duration with its tick count scaled by a double |
| `value.Divide(divisor)` with a double | A duration with its tick count divided by the scalar |
| `value.Divide(other)` with a TimeSpan | A double ratio of the complete durations |

```csharp
using Standard::Time;

int main()
{
    TimeSpan interval = TimeSpan::FromSeconds(3);
    TimeSpan half = interval.Multiply(0.5);
    TimeSpan quarter = interval.Divide(4.0);
    if (half.Ticks != 15000000l || quarter.Ticks != 7500000l) return 1;
    if (interval.Divide(half) != 2.0) return 2;
    if (TimeSpan::FromTicks(-5l).Duration().Ticks != 5l) return 3;

    // Scaling rounds a half tick to the nearest even tick count.
    if (TimeSpan::FromTicks(5l).Multiply(0.5).Ticks != 2l) return 4;
    if (TimeSpan::FromTicks(7l).Divide(2.0).Ticks != 4l) return 5;
    return 0;
}
```

Freestanding `Multiply(value, factor)` and `Divide(value, divisor)` forms are
also available.

## Rounding and errors

Scaling converts the tick count to double, performs the operation, then rounds
to the nearest whole tick with midpoint ties to even. This differs from
[fractional factories](TIME_SPAN_FRACTIONAL.md), which truncate fractional ticks.
Large tick counts can lose precision during the double conversion. Even
scaling by one can therefore change a large duration by a few ticks.

The scalar overloads reject a NaN argument with
`Exception("TimeSpan factor must not be NaN")` or
`Exception("TimeSpan divisor must not be NaN")`. A result that is NaN, infinite,
or outside the tick range throws `Exception("TimeSpan scaling overflow")`.
Dividing by either signed zero therefore throws. Division by infinity produces
zero ticks.

As with the fractional factories, the floating-point upper endpoint of 2^63
maps to the maximum tick count before integer conversion. The negative
endpoint maps to the minimum tick count. Failures occur before a result is
assigned, and the source duration stays unchanged.

## Ratios and source selection

Dividing by another `TimeSpan` uses floating-point division and is `nothrow`.
A nonzero duration divided by zero produces the corresponding infinity.
Zero divided by zero produces NaN. The result can lose precision because tick
counts are converted to double first.

Successful duration arithmetic does not allocate. Selective builds add
`Time/time_span_scaling.gf` alongside `Time/TimeSpan.gf`
to use the scaling extensions. `Duration()` is part of the base type and has
no dependency on the scaling extension.
