# Time intervals

`Standard::Time::TimeSpan` is a signed duration stored as one private `long` tick count.
One tick is 100 nanoseconds. Every signed 64-bit tick value is valid, and default
construction represents zero. It has no calendar, clock, timezone, or OS dependency.

```csharp
using Standard::Text;
using Standard; using Standard::Time;
int main()
{
    TimeSpan travel = TimeSpan::FromMinutes(90);
    TimeSpan remaining = travel.Subtract(TimeSpan::FromSeconds(30));
    String message = s$"Remaining: {remaining}";
    if (!message.Equals("Remaining: 01:29:30")) return 1;
    return 0;
}
```

`FromTicks(long)` is `nothrow` and exact. `FromMilliseconds(long)`,
`FromSeconds(long)`, `FromMinutes(long)`, `FromHours(long)`, and `FromDays(long)`
accept whole signed units, multiply by their tick scale, and throw `Exception`
if the result cannot fit in `long`. They check bounds before multiplication.
There are no floating point factory overloads or implicit unit conversions.

Immutable `Ticks`, `Days`, `Hours`, `Minutes`, `Seconds`, and `Milliseconds`
properties expose the exact count and signed components. Division truncates
toward zero: minus 25 hours has `Days == -1` and `Hours == -1`, while minus one
tick has zero whole components. Hours range from -23 through 23, minutes and
seconds from -59 through 59, and milliseconds from -999 through 999.
`TotalMilliseconds`, `TotalSeconds`, `TotalMinutes`, `TotalHours`, and `TotalDays`
return the complete interval as an approximate `double` in that unit, including
fractional units. They are immutable, `nothrow`, and allocate nothing. They are
totals rather than components: 25 hours has `Hours == 1` and `TotalHours == 25`;
minus one tick has zero whole components and negative fractional totals.
Each converts ticks to `double` and divides by the unit's tick scale. Large
values can lose tick precision; use `Ticks` for exact storage or arithmetic.
The natural approximate result is not clamped to a whole-unit integer range.

```cpp
using Standard; using Standard::Time;
int main()
{
    TimeSpan interval = TimeSpan::FromTicks(15000l);
    if (interval.TotalMilliseconds != 1.5d || interval.Milliseconds != 1) return 1;
    return 0;
}
```

For a stopwatch, `watch.Elapsed.TotalMilliseconds` observes its recorded elapsed
duration in milliseconds without changing the clock provider or allocating.

`Add(TimeSpan)`, `Subtract(TimeSpan)`, and `Negate()` return new values and check
for overflow before performing signed arithmetic. They throw `Exception` when
the result would lie outside the signed tick range. In particular, negating
`FromTicks(-9223372036854775807l - 1l)` throws because its positive counterpart
does not fit. Successful factories and arithmetic allocate nothing; throwing
operations follow the ordinary exception allocation contract.

## Formatting

`TryFormat(output, capacity, ref written)` writes invariant ASCII in the form
`[-][d.]hh:mm:ss[.fffffff]`, without a null terminator. Zero days and a zero
fraction are omitted. A nonzero fraction always has exactly seven digits.
Examples are `00:00:00`, `-00:00:00.0000001`, `1.01:00:00`, and
`-10675199.02:48:05.4775808` (the minimum tick value).

Formatting uses at most 26 bytes. It is `nothrow` and allocates nothing.
Insufficient capacity returns false, sets `written` to zero, and leaves every
output byte untouched. Zero capacity does not access the output pointer.
Supply writable output storage covering the stated capacity. The output region
may overlap the receiver when backed by enough writable storage: all bytes are
prepared in a separate stack buffer before output is modified. The `written`
location must be separate from both output and receiver storage.

`IFormattable.Format(writer)` writes the same representation with one counted
write from stack storage. It borrows the writer and neither retains it nor
flushes or closes it. Writer failures propagate and may leave partial output.
TimeSpan itself has no String dependency; ordinary interpolation can use its
formatting interface. Allocating an interpolated String follows String's usual
allocation behavior.

## Parsing

`TimeSpan::TryParse(text, count, &result)` accepts a complete invariant ASCII
interval in the form `[-][days.]hh:mm:ss[.fraction]`. Hours, minutes, and seconds
each require exactly two decimal digits. Hours range from `00` through `23`;
minutes and seconds from `00` through `59`. Optional days contain one or more
decimal digits, including leading zeros. An optional fractional second contains
one through seven digits, padded on the right to seven tick digits:
`00:00:01.5` is one and a half seconds. Formatting still emits seven fractional
digits whenever the fraction is nonzero.

Both signed tick endpoints are supported, including
`-10675199.02:48:05.4775808`. Magnitudes outside the signed range fail;
`10675199.02:48:05.4775808` is therefore invalid. A leading minus applies to the
complete interval; negative zero becomes zero. A plus sign, whitespace, embedded
NUL, trailing data, omitted clock components, and unit words are rejected.

Counted input is at most 26 bytes, including any day-prefix leading zeros.
Zero length or a length above 26 fails without reading input.
`TryParse(cString, &result)` scans at most 27 bytes to check the terminator.
The Try methods are `nothrow`, allocate nothing, and reset the result to zero on
failure. All needed input is consumed before committing the result, so readable
input may overlap properly aligned writable result storage. A failure reset can
also overwrite overlapping input bytes. Input pointers are borrowed, not retained.

`TimeSpan::Parse(text, count)` and `TimeSpan::Parse(cString)` return the parsed
value or throw `Exception("Invalid time interval")`. Successful parsing allocates
nothing. The grammar is an explicit serialization format, without locale,
floating point conversion, or clock dependencies.

```csharp
using Standard::Text;
using Standard; using Standard::Time;
int main()
{
    TimeSpan interval = TimeSpan::Parse("-01.02:03:04.5");
    String canonical = s$"{interval}";
    if (!canonical.Equals("-1.02:03:04.5000000")) return 1;
    return 0;
}
```

## Equality and ordering

`IEquatable<TimeSpan>`, `IComparable<TimeSpan>`, and `IHashable` use the exact
signed tick count. Comparison returns -1, 0, or 1 without subtracting the counts,
so ordering works across the full range. Equal values have equal hashes.
Hashes serve collections and are not a stable serialized representation.
Use `Ticks` when persistence matters. No arithmetic operators, clocks,
or calendar arithmetic are provided.

For selective source use, include `core/Time/TimeSpan.gf`, `core/ITextWriter.gf`, `core/IFormattable.gf`,
`core/IEquatable.gf`, `core/IComparable.gf`, and `core/IHashable.gf` with
`core/Hashing/primitive_hashing.gf` and their normal Exception/runtime dependencies.

The tick unit, signed components, and constant spelling match documented
[.NET TimeSpan semantics](https://learn.microsoft.com/en-us/dotnet/api/system.timespan)
and the [constant format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings).
Native tests use .NET as an independent result oracle. The implementation is
original gflat source and does not incorporate .NET runtime code.
