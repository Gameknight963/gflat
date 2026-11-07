# Constructing dates and timestamps from components

`DateTime` accepts year, month, and day, followed by optional hour, minute,
second, millisecond, and microsecond components. Omitted clock components
are zero.

```csharp
using Standard::Time;

int main()
{
    DateTime date = new(2024, 2, 29);
    DateTime precise = new(2024, 2, 29, 12, 34, 56, 789, 123);
    DateTimeOffset stamp = new(2024, 2, 29, 12, 34, 56, 789, 123, TimeSpan::FromHours(2l));
    if (date.Hour != 0 || precise.Microsecond != 123) return 1;
    if (stamp.UtcDateTime.Hour != 10 || stamp.Ticks != precise.Ticks) return 2;
    return 0;
}
```

`DateTimeOffset` accepts year, month, day, hour, minute, and second followed by
an offset. Overloads insert a millisecond component, or both millisecond and
microsecond components, before that offset.

Dates must be valid Gregorian dates in years 1 through 9999. Clock fields
require hour 0 through 23, minute and second 0 through 59, and millisecond and
microsecond 0 through 999. Components are validated rather than normalized.
For precision below one microsecond, use the existing tick-based APIs.

Offset construction retains the existing rules: a whole-minute offset between
-14 and +14 hours, and both wall clock and UTC clock within the supported
calendar range. No named time zone or daylight-saving adjustment is implied.
Invalid construction throws before an assignment destination is replaced.
Successful construction does not allocate.

The existing constructors taking `Date`/`TimeOfDay` or `DateTime`/`TimeSpan`
remain available. All component constructors belong to the core type sources.
