# Read date and clock components together

`Date.Deconstruct(ref year, ref month, ref day)` extracts all three Gregorian
components in one calendar decomposition. `TimeOfDay.Deconstruct` has overloads
with two through five `ref int` outputs, in this order: hour, minute, second,
millisecond, microsecond.

```csharp
using Standard::Time;

int main()
{
    Date date = new(2024, 2, 29);
    int year = 0;
    int month = 0;
    int day = 0;
    date.Deconstruct(ref year, ref month, ref day);
    if (year != 2024 || month != 2 || day != 29) return 1;
    TimeOfDay clock = new(13, 45, 30, 123, 456);
    int hour = 0;
    int minute = 0;
    clock.Deconstruct(ref hour, ref minute);
    if (hour != 13 || minute != 45) return 2;
    return 0;
}
```

These are ordinary method calls. They do not introduce tuple assignment syntax.
Clock millisecond and microsecond outputs are components from 0 through 999,
not totals. Sub-microsecond ticks are omitted without rounding. All overloads
are allocation-free and `nothrow`. Outputs are written in parameter order.

Selective builds use `Time/Date.gf` or `Time/TimeOfDay.gf` and their usual
interface, formatting, and calendar dependencies.
