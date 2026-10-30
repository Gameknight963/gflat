# Calendar dates

`Standard::Time::Date` is an immutable Gregorian calendar value from `0001-01-01` through
`9999-12-31`. It has no clock, time-of-day, timezone, locale, or OS dependency.
The Gregorian rules apply throughout that range, including before their historical
adoption. Its single private `int` day number counts days since `0001-01-01`;
the supported-range bound is a static constant and adds no per-date storage.
An empty/default construction represents that first day, rather than an invalid date.

```csharp
using Standard::Text;
using Standard; using Standard::Time;
Date leapDay = new(2024, 2, 29);
Date march = leapDay.AddDays(1);
String message = s$"Next day: {march}"; // Next day: 2024-03-01
```

`Date(year, month, day)` validates all components. Readonly `Year`, `Month`, `Day`,
and `DayNumber` properties expose them. `Date::FromDayNumber(number)` accepts
`0..3652058`. `Date::IsLeapYear(year)` and `Date::DaysInMonth(year, month)` validate
the same year/month ranges. Invalid arguments throw `Exception`.

`DayOfWeek` returns `Standard::Time::DayOfWeek`: `Sunday = 0`, `Monday = 1`, through
`Saturday = 6`. `DayOfYear` is one-based, from `1` on January 1 through `365`
(or `366` in a leap year). Both properties are `immutable`, `nothrow`, and
allocate nothing. These are calendar facts; neither consults a clock or timezone.

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
using Standard::Time;
Date invoice = new(2024, 1, 31);
Date nextInvoice = invoice.AddMonths(1); // 2024-02-29
Date anniversary = nextInvoice.AddYears(1); // 2025-02-28
```

Clamping does not remember an original end-of-month preference. Calling
`invoice.AddMonths(2)` gives March 31, whereas
`invoice.AddMonths(1).AddMonths(1)` gives March 29. Base recurring schedules on
the original date when that distinction matters. These calendar shifts differ
from adding a fixed number of days.

## ISO week dates

Immutable `IsoWeekYear` and `IsoWeekOfYear` expose ISO week numbering without
changing the stored Gregorian date. ISO weeks begin on Monday; week 1 contains
January 4, equivalently the year's first Thursday. Dates near New Year can
belong to an adjacent ISO year: `2021-01-01` is week 53 of ISO year 2020.
Both properties are `nothrow` and allocate nothing. Default Date is Gregorian
`0001-01-01`, also Monday in ISO year 1, week 1.

`Date::GetIsoWeeksInYear(year)` accepts ISO years `1..9999` and returns 52 or 53.
`Date::FromIsoWeek(year, week, weekday)` returns the corresponding Gregorian date.
It requires a week from 1 through that ISO year's actual week count and a valid
`DayOfWeek` value. The existing enum keeps its values: Sunday is 0, Monday is 1,
through Saturday 6. Sunday is the final day of an ISO week.

Invalid years, nonexistent week 53, invalid weekday values, and results outside
the supported Gregorian Date range throw `Exception`. In particular, Saturday
and Sunday of ISO year 9999's final week lie beyond `9999-12-31` and are rejected.
Successful factories and week-count lookup allocate nothing. These methods
perform calendar calculations without a clock, locale, or timezone.

```csharp
using Standard; using Standard::Time;
int main()
{
    Date holiday = Date::FromIsoWeek(2020, 53, DayOfWeek::Friday);
    if (holiday.Year != 2021 || holiday.Month != 1 || holiday.Day != 1) return 1;
    if (holiday.IsoWeekYear != 2020 || holiday.IsoWeekOfYear != 53) return 2;
    if (Date::GetIsoWeeksInYear(2021) != 52) return 3;
    return 0;
}
```

The ISO fields and valid conversions can be checked against Microsoft's
[ISOWeek API](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.isoweek).
The factory here explicitly validates that the requested week exists in
the selected ISO year, in addition to validating the resulting Gregorian range.

## Formatting

`TryFormat(output, capacity, ref written)` emits exactly ten ASCII bytes in
`yyyy-MM-dd` form, without a null terminator. It is `nothrow` and allocates nothing.
Insufficient capacity returns false, sets `written` to zero, and leaves all output
bytes unchanged. Zero capacity does not access output. Keep the `written` location
disjoint from both the output buffer and receiver storage, and supply writable
storage for the stated capacity. Output may overlap the receiver when backed by
enough writable bytes: all calendar components are captured before output writes.
A successful overlapping write replaces those receiver bytes with text; it does
not preserve the date object stored there.

`IFormattable.Format(writer)` sends that same ten-byte representation in one
counted write using stack storage; it does not allocate a String or flush/close the
writer. Writer failures propagate and may already have produced partial output.
Ordinary string interpolation uses this interface, so `s$"{date}"` works without
`ToString()` or a String dependency inside Date. Creating the interpolated String
itself follows its usual allocation behavior.

## Parsing

`Date::TryParse(text, count, &result)` accepts exactly ten ASCII bytes in
`yyyy-MM-dd` form, with valid Gregorian components. It accepts neither whitespace,
signs, omitted leading zeroes, trailing data, nor timestamps. Year zero and
impossible dates such as `1900-02-29` fail. It is `nothrow` and allocates nothing.
Failure returns false and resets `result` to the default date, `0001-01-01`.
When `count != 10`, input is not read, including at zero length.

`Date::TryParse(cString, &result)` accepts the same format terminated by NUL.
It examines at most eleven bytes; bytes after the first NUL are outside the
C string. A counted input includes any embedded NUL and therefore rejects it.
Supply readable input and a writable, properly aligned `Date*` result. Result
storage may overlap input: all needed input is read before the output is committed.

`Date::Parse(text, count)` and `Date::Parse(cString)` return the parsed date or
throw `Exception` on invalid input. Successful parsing allocates nothing.

```csharp
using Standard::Time;
Date birthday = Date::Parse("2000-02-29");
Date parsed = new();
if (Date::TryParse("2024-12-31", &parsed)) {
    Date nextYear = parsed.AddYears(1);
}
```

The sources are `core/Time/Date.gf` and `core/Time/DayOfWeek.gf`, with the normal
equality/comparison/hash and text interface dependencies. The complete standard library includes them. No
current-date lookup, custom formats, other calendars, or timezone conversion is
provided by this API.

The supported range and day-number baseline also match
[.NET DateOnly](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly), which
the native tests use as an independent calendar and formatting oracle.
Month/year arithmetic is also checked against
[DateOnly.AddMonths](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly.addmonths)
and [DateOnly.AddYears](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly.addyears).
