# Measure elapsed time

`Standard::Time::Stopwatch<C>` accumulates elapsed time using a clock provider
that implements static `IMonotonicClock` members:

- `long GetTimestamp()` reads a counter
- `long GetFrequency()` gives its fixed positive ticks-per-second frequency

For example, use a controlled clock to test timing behavior:

```csharp
using Standard;
using Standard::Time;

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

## Start, stop, and reset

Construction caches frequency and starts stopped. Start resumes an accumulated
measurement, and Stop freezes it. Starting an already running timer or stopping
a stopped one keeps its state and avoids reading the clock.

Reset clears time and stops. Restart reads a new starting timestamp and clears
earlier time. `StartNew()` constructs and starts a timer in one call.

## Read elapsed time

| Property | Result |
| --- | --- |
| `IsRunning` | Current running state |
| `ElapsedTicks` | Accumulated provider counter ticks |
| `ElapsedSeconds` | Approximate double seconds |
| `Elapsed` | TimeSpan rounded down to complete 100-nanosecond ticks |

Each elapsed property reads the clock once while running and uses stored state
while stopped. Provider ticks can differ from TimeSpan ticks. Exact TimeSpan
conversion can overflow even when ElapsedTicks still fits. ElapsedSeconds
remains available in that case.

## Providers and errors

Timestamps can start negative, but must not move backwards or wrap during a
measurement. Invalid frequency, backwards readings, and accumulated or
TimeSpan overflow throw. Failed Start, Stop, and Restart preserve timer state.
Reset can clear a failed measurement.

Optional native providers are available for [Windows x64](../libraries/platform/windows/README.md)
and [Linux LP64](../libraries/platform/linux/README.md). Add the appropriate
project reference and choose the provider as the type argument, for example
`alias Timer = Standard::Time::Stopwatch<Windows::PerformanceCounter>;`.
Behavior during system suspension depends on the selected provider.
