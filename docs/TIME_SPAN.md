# Time intervals

`Standard::Time::TimeSpan` stores a signed duration in 100-nanosecond ticks.
Every signed 64-bit tick count is valid. Default construction gives zero.

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

int main()
{
    TimeSpan travel = TimeSpan::FromMinutes(90);
    TimeSpan remaining = travel.Subtract(TimeSpan::FromSeconds(30));
    String message = s$"Remaining: {remaining}";
    if (!message.Equals("Remaining: 01:29:30")) return 1;
    return 0;
}
```

## Units and components

FromTicks accepts an exact long. FromMilliseconds, FromSeconds, FromMinutes,
FromHours, and FromDays accept whole signed units and throw if the scaled
tick count overflows.

Days, Hours, Minutes, Seconds, and Milliseconds expose signed components.
Division truncates toward zero, so minus 25 hours has Days = -1 and Hours = -1.
TotalMilliseconds, TotalSeconds, TotalMinutes, TotalHours, and TotalDays return
the complete duration as a double, including fractions:

```cpp
using Standard;
using Standard::Time;

int main()
{
    TimeSpan interval = TimeSpan::FromTicks(15000l);
    if (interval.TotalMilliseconds != 1.5d || interval.Milliseconds != 1) return 1;
    return 0;
}
```

Large totals can lose tick precision. Use Ticks for exact storage and
arithmetic. Add, Subtract, and Negate throw if their result exceeds the signed
tick range. Negating the minimum tick value therefore throws.

## Formatting

TryFormat writes `[-][d.]hh:mm:ss[.fffffff]` as counted ASCII bytes. Zero days
and zero fractions are omitted. A nonzero fraction always has seven digits.
For example, minus one tick formats as `-00:00:00.0000001`.

Output needs at most 26 bytes. Insufficient capacity returns false, resets
`written` to zero, and preserves output. Format and interpolation use the same
representation.

## Parsing

TryParse and Parse accept `[-][days.]hh:mm:ss[.fraction]`. Clock components
require two digits, hours must be below 24, and minutes and seconds below 60.
Fractions have one through seven digits. `00:00:01.5` means 1.5 seconds.

The complete text must fit 26 bytes, including leading zeroes in days. A plus
sign, whitespace, embedded NULs, trailing data, and omitted clock components
are invalid. Negative zero becomes zero. Both tick endpoints are accepted,
including `-10675199.02:48:05.4775808`.

TryParse resets the result to zero on failure. Parse throws
`Exception("Invalid time interval")`. The C-string overload examines at most
27 bytes. Counted lengths zero or above 26 are rejected before reading input.

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

int main()
{
    TimeSpan interval = TimeSpan::Parse("-01.02:03:04.5");
    String canonical = s$"{interval}";
    if (!canonical.Equals("-1.02:03:04.5000000")) return 1;
    return 0;
}
```

TryParse and TryFormat are `nothrow`. They stage their data, so result or
receiver storage can overlap sufficiently backed input or output bytes. Keep
`written` separate from output and receiver.

Equality, ordering, and hashing use the exact tick count. Source:
`core/Time/TimeSpan.gf`, with formatting and interface dependencies.
The units and spelling follow [.NET TimeSpan](https://learn.microsoft.com/en-us/dotnet/api/system.timespan)
and its [constant format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings).
