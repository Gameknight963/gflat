# Time intervals

`std::TimeSpan` is a signed duration stored as one private `long` tick count.
One tick is 100 nanoseconds. Every signed 64-bit tick value is valid, and default
construction represents zero. It has no calendar, clock, timezone, or OS dependency.

```csharp
using std;
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
`TotalSeconds` returns the whole interval as an approximate `double`, including
fractional seconds; large values can lose tick precision. Use `Ticks` for exact
storage or arithmetic.

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

## Equality and ordering

`IEquatable<TimeSpan>`, `IComparable<TimeSpan>`, and `IHashable` use the exact
signed tick count. Comparison returns -1, 0, or 1 without subtracting the counts,
so ordering works across the full range. Equal values have equal hashes.
Hashes serve collections and are not a stable serialized representation.
Use `Ticks` when persistence matters. No arithmetic operators, parsing, clocks,
or calendar arithmetic are provided.

For selective source use, include `core/TimeSpan.gf`, `core/Text.gf`,
`core/IEquatable.gf`, `core/IComparable.gf`, and `core/IHashable.gf` with
`core/Hashing.gf` and their normal Exception/runtime dependencies.

The tick unit, signed components, and constant spelling match documented
[.NET TimeSpan semantics](https://learn.microsoft.com/en-us/dotnet/api/system.timespan)
and the [constant format](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings).
Native tests use .NET as an independent result oracle. The implementation is
original gflat source and does not incorporate .NET runtime code.
