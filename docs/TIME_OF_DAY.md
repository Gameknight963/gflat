# Time of day

`Standard::Time::TimeOfDay` represents midnight through `23:59:59.9999999`,
at 100-nanosecond resolution. Default construction gives midnight.

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

int main()
{
    TimeOfDay opening = new(9, 30);
    TimeOfDay closing = TimeOfDay::Parse("17:45:00.125");
    if (opening.CompareTo(&closing) >= 0) return 1;
    String label = s$"{opening} - {closing}";
    if (!label.Equals("09:30:00 - 17:45:00.1250000")) return 2;
    int days = 0;
    TimeOfDay next = closing.Add(TimeSpan::FromHours(8), ref days);
    if (days != 1 || next.Hour != 1 || next.Minute != 45) return 3;
    if (!next.IsBetween(new TimeOfDay(23, 0), new TimeOfDay(2, 0))) return 4;
    return 0;
}
```

## Construction and arithmetic

The constructor takes hours, minutes, optional seconds, and optional
milliseconds. Their ranges are 0â€“23, 0â€“59, 0â€“59, and 0â€“999.
`FromTicks(long)` accepts `0..863999999999`. Invalid components throw.

Properties expose Ticks, Hour, Minute, Second, and Millisecond. Add accepts a
signed TimeSpan and wraps within the day. `Add(value, ref wrappedDays)` also
reports signed day crossings. Midnight minus one tick wraps to the last tick
of the previous day with `wrappedDays == -1`. Every TimeSpan tick value is
accepted.

`IsBetween(start, end)` includes start and excludes end. A later start than
end crosses midnight. Equal endpoints form an empty interval.

## Parsing and formatting

Parse and TryParse accept `HH:mm:ss[.fraction]`, with exactly two digits per
clock component and one through seven fraction digits. Whitespace, signs,
`24:00:00`, and leap seconds are invalid. TryParse resets its result to
midnight on failure. The C-string form examines at most 17 bytes.

TryFormat writes eight bytes for whole seconds or sixteen with a nonzero
seven-digit fraction. Output is counted. Insufficient capacity returns false,
sets `written` to zero, and preserves the buffer. Format and interpolation use
the same representation.

TryParse and TryFormat are `nothrow`. Parsing and formatting stage their data,
allowing overlap with properly backed result or receiver storage. Keep
`written` separate from receiver and output.

Source: `Time/TimeOfDay.gf`, with TimeSpan and interface dependencies.
Arithmetic follows [TimeOnly.Add](https://learn.microsoft.com/en-us/dotnet/api/system.timeonly.add)
and [TimeOnly.IsBetween](https://learn.microsoft.com/en-us/dotnet/api/system.timeonly.isbetween).
