# Calendar dates

`Standard::Time::Date` represents Gregorian dates from `0001-01-01` through
`9999-12-31`. The Gregorian rules apply throughout that range, including dates
before their historical adoption.

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

Date leapDay = new(2024, 2, 29);
Date march = leapDay.AddDays(1);
String message = s$"Next day: {march}"; // Next day: 2024-03-01
```

## Construction and components

`Date(year, month, day)` checks the components. Default construction gives
`0001-01-01`. `FromDayNumber(number)` accepts `0..3652058`, counting from that
first day. Invalid arguments throw `Exception`.

Properties expose `Year`, `Month`, `Day`, `DayNumber`, `DayOfYear`, and
`DayOfWeek`. DayOfYear starts at one. DayOfWeek uses Sunday = 0 through
Saturday = 6. `IsLeapYear` and `DaysInMonth` validate their year and month.

## Calendar arithmetic

`AddDays`, `AddMonths`, and `AddYears` return the shifted date and throw if it
would leave the supported range. Month and year changes clamp the day to the
destination month's final day:

```csharp
using Standard::Time;

Date invoice = new(2024, 1, 31);
Date nextInvoice = invoice.AddMonths(1); // 2024-02-29
Date anniversary = nextInvoice.AddYears(1); // 2025-02-28
```

Clamping uses the current day. January 31 plus two months gives March 31,
while two successive one-month additions can give March 28 or 29. Base a
recurring schedule on its original date when that distinction matters.

## ISO week dates

`IsoWeekYear` and `IsoWeekOfYear` use weeks beginning Monday. Week 1 contains
January 4. Near New Year, the ISO year can differ from the calendar year.

`GetIsoWeeksInYear(year)` returns 52 or 53. `FromIsoWeek(year, week, weekday)`
requires an existing week and valid DayOfWeek. Results must also fit the Date
range. For example, Saturday and Sunday of ISO year 9999's last week lie
beyond `9999-12-31` and are rejected.

```csharp
using Standard;
using Standard::Time;

int main()
{
    Date holiday = Date::FromIsoWeek(2020, 53, DayOfWeek::Friday);
    if (holiday.Year != 2021 || holiday.Month != 1 || holiday.Day != 1) return 1;
    if (holiday.IsoWeekYear != 2020 || holiday.IsoWeekOfYear != 53) return 2;
    if (Date::GetIsoWeeksInYear(2021) != 52) return 3;
    return 0;
}
```

## Formatting

`TryFormat(output, capacity, ref written)` writes ten ASCII bytes,
`yyyy-MM-dd`, without a terminator. It is `nothrow`. Insufficient capacity
returns false, sets `written` to zero, and preserves output.

Output can overlap sufficiently backed receiver storage because formatting
captures its components first. Keep `written` separate from receiver and
output. `Format(writer)` and interpolation use the same representation.

## Parsing

`TryParse(text, count, &result)` and the C-string overload require exactly
`yyyy-MM-dd` with a valid Gregorian date. Failure resets the result to
`0001-01-01`. Both are `nothrow`. A counted length other than ten is rejected
before reading input. The C-string form examines at most eleven bytes.

Parse returns the value or throws `Exception`. Result storage can overlap
input because parsing reads before assignment.

```csharp
using Standard::Time;

Date birthday = Date::Parse("2000-02-29");
Date parsed = new();
if (Date::TryParse("2024-12-31", &parsed)) {
    Date nextYear = parsed.AddYears(1);
}
```

Equality, ordering, and hashing use the day number. Sources:
`core/Time/Date.gf` and `core/Time/DayOfWeek.gf`, with their interface
dependencies. Calendar behavior follows [.NET DateOnly](https://learn.microsoft.com/en-us/dotnet/api/system.dateonly)
and [ISOWeek](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.isoweek).
