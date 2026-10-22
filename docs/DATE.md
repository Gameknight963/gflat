# Calendar dates

`std::Date` is an immutable Gregorian calendar value from `0001-01-01` through
`9999-12-31`. It has no clock, time-of-day, timezone, locale, or OS dependency.
The Gregorian rules apply throughout that range, including before their historical
adoption. Its single private `int` day number counts days since `0001-01-01`.
An empty/default construction represents that first day, rather than an invalid date.

```csharp
using std;
Date leapDay = new(2024, 2, 29);
Date march = leapDay.AddDays(1);
String message = s$"Next day: {march}"; // Next day: 2024-03-01
```

`Date(year, month, day)` validates all components. Readonly `Year`, `Month`, `Day`,
and `DayNumber` properties expose them. `Date::FromDayNumber(number)` accepts
`0..3652058`. `Date::IsLeapYear(year)` and `Date::DaysInMonth(year, month)` validate
the same year/month ranges. Invalid arguments throw `Exception`.

`AddDays(int days)` returns a new date; it never mutates the original. It uses a
64-bit intermediate and throws if the result leaves the supported range, including
for extreme 32-bit offsets. It does not wrap, clamp, or normalize invalid dates.
Copying a Date copies its small value without allocating. `IEquatable<Date>`,
`IComparable<Date>`, and `IHashable` use the day number for equality, chronological
ordering, and hashing. Hashes are not a serialized representation; use `DayNumber`
or the documented calendar components when persistence matters.

`AddMonths(int months)` and `AddYears(int years)` perform calendar arithmetic.
Offsets may be positive, negative, or zero. If the destination month has fewer
days, the day is clamped to its last day: January 31 plus one month becomes
February 28 (or 29), and February 29 plus one year becomes February 28.
Results outside years `1..9999` throw `Exception`; extreme offsets never wrap.
Both methods preserve the original date and allocate nothing on success.

```csharp
Date invoice = new(2024, 1, 31);
Date nextInvoice = invoice.AddMonths(1); // 2024-02-29
Date anniversary = nextInvoice.AddYears(1); // 2025-02-28
```

Clamping does not remember an original end-of-month preference. Calling
`invoice.AddMonths(2)` gives March 31, whereas
`invoice.AddMonths(1).AddMonths(1)` gives March 29. Base recurring schedules on
the original date when that distinction matters. These calendar shifts differ
from adding a fixed number of days.

## Formatting

`TryFormat(output, capacity, ref written)` emits exactly ten ASCII bytes in
`yyyy-MM-dd` form, without a null terminator. It is `nothrow` and allocates nothing.
Insufficient capacity returns false, sets `written` to zero, and leaves all output
bytes unchanged. Zero capacity does not access output. Keep the `written` location
disjoint from the output buffer, and supply writable storage for the stated capacity.

`IFormattable.Format(writer)` sends that same ten-byte representation in one
counted write using stack storage; it does not allocate a String or flush/close the
writer. Writer failures propagate and may already have produced partial output.
Ordinary string interpolation uses this interface, so `s$"{date}"` works without
`ToString()` or a String dependency inside Date. Creating the interpolated String
itself follows its usual allocation behavior.

The source is `core/Date.gf`, with the normal equality/comparison/hash and text
interface dependencies. The complete standard library includes them. No parsing,
current-date lookup, custom formats, other calendars, or timezone conversion is
provided by this API.

The supported range and day-number baseline also match
[.NET DateOnly](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly), which
the native tests use as an independent calendar and formatting oracle.
Month/year arithmetic is also checked against
[DateOnly.AddMonths](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly.addmonths)
and [DateOnly.AddYears](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly.addyears).
