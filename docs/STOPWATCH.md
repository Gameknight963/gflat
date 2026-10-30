# Measuring intervals

`Standard::Time::Stopwatch<C>` is a value that accumulates elapsed time from a clock provider.
The provider implements the static `IMonotonicClock` interface:
`long GetTimestamp()` and `long GetFrequency()`. Frequency is positive, fixed,
and expressed in counter ticks per second. Timestamp origins may be negative;
readings must not move backwards or wrap during a measurement.

The application selects the provider as an ordinary type argument. This complete
example uses a deterministic clock and requires no operating-system calls:

```csharp
using Standard; using Standard::Time;
long timestamp = 0l;
static class Clock : IMonotonicClock
{
    public static long GetTimestamp() => timestamp;
    public static long GetFrequency() => 10l;
}
int main()
{
    Stopwatch<Clock> timer = new();
    timer.Start();
    timestamp = 25l;
    timer.Stop();
    if (timer.ElapsedTicks != 25l || timer.ElapsedSeconds != 2.5) return 1;
    TimeSpan elapsed = timer.Elapsed;
    if (elapsed.Ticks != 25000000l) return 2;
    timer.Reset();
    if (timer.IsRunning || timer.ElapsedTicks != 0l) return 3;
    return 0;
}
```

Construction reads and caches frequency, starts stopped, and does not sample a
timestamp. `StartNew()` constructs and starts a new value. Start resumes an
accumulated measurement; Stop freezes it. Repeating Start while running or Stop
while stopped does nothing and does not consult the provider. Reset clears the
accumulated time and stops without consulting the provider. Restart samples a
new starting timestamp and clears earlier elapsed time.

Copies have independent accumulated/running state. The provider type is shared,
but there is no timer handle to dispose. Successful state operations allocate
nothing themselves; a custom provider can have its own costs.

| Observer | Meaning |
| --- | --- |
| `IsRunning` | Current running state; immutable and `nothrow`. |
| `ElapsedTicks` | Accumulated raw counter ticks, including the current running interval. |
| `ElapsedSeconds` | Approximate double-precision seconds. |
| `Elapsed` | A TimeSpan, rounded down to complete 100-nanosecond ticks. |

The raw units of ElapsedTicks depend on the provider and can differ from
TimeSpan ticks. Each elapsed observer samples at most once while running, and
does not sample while stopped. Exact TimeSpan conversion uses 64-bit integer
arithmetic even for frequencies near the signed-long limit. It can overflow
when raw ElapsedTicks remains representable; ElapsedSeconds remains available.

Invalid frequency, backwards readings, counter/accumulation overflow, and
TimeSpan overflow throw Exception. Provider failures propagate. Failed Start,
Stop, or Restart leaves the timer's fields unchanged; elapsed getters never
modify them. Reset is `nothrow` and can clear a failed measurement. The core
source uses no String or libc imports and selects no global clock hook.

## Native providers

Optional source-library projects supply native providers:

- [Windows performance counter](../libraries/platform/windows/README.md): Windows x64.
- [Linux monotonic clock](../libraries/platform/linux/README.md): Linux LP64.

Add the appropriate project reference, then give the chosen specialization a
local alias, for example `alias Timer = Standard::Time::Stopwatch<Windows::PerformanceCounter>;`.
These projects are distributed under `libraries/platform` in the SDK and are
not automatically included with Standard. Their clocks measure intervals; their
origins are unrelated to calendar dates. Platform behavior during system
suspension belongs to the selected provider's contract.
