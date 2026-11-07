# Measuring between timestamps

`Stopwatch<C>::GetTimestamp()` reads the selected provider directly.
`GetElapsedTime(start)` measures from a saved timestamp to a new sample, while
`GetElapsedTime(start, end)` converts an explicit interval without reading a
timestamp.

```csharp
using Standard::Time;

long counter = 0l;
static class Clock : IMonotonicClock
{
    public static long GetTimestamp() => counter;
    public static long GetFrequency() => 3l;
}

int main()
{
    long start = Stopwatch<Clock>::GetTimestamp();
    counter = 4l;
    TimeSpan elapsed = Stopwatch<Clock>::GetElapsedTime(start);
    if (elapsed.Ticks != 13333333l) return 1;
    TimeSpan reversed = Stopwatch<Clock>::GetElapsedTime(counter, start);
    if (reversed.Ticks != -13333333l) return 2;
    if (Stopwatch<Clock>::Frequency != 3l) return 3;
    return 0;
}
```

## Provider calls and units

`Frequency` returns the provider's positive counter ticks per second, checking
it on each access. `GetTimestamp()` reads one timestamp without querying frequency.
`GetElapsedTime(start, end)` queries frequency once and validates it. The one-argument
form reads a timestamp first, then uses that same conversion. Provider failures
propagate, and frequency must be positive even for a zero interval.

Both timestamps must use the same provider and frequency. Differences are
computed across the full signed timestamp range without intermediate overflow.
An explicit end earlier than the start produces a negative duration. Conversion
is exact integer arithmetic truncated toward zero to whole 100-nanosecond ticks.
Results outside the signed `TimeSpan` range throw. Valid calls allocate no storage
and do not require a wide-integer division runtime.

## Whole milliseconds from an instance

`timer.ElapsedMilliseconds` returns elapsed whole milliseconds as a `long`, rounded
down. It reads one timestamp while running and none while stopped, using the
frequency cached when the timer was constructed. Its range is independent of
`timer.Elapsed`, so milliseconds can remain available when a `TimeSpan` result
would overflow. A millisecond result outside the signed `long` range throws.

The instance's existing monotonicity rules still apply: a running timer rejects
backwards clock samples. See [measuring intervals](STOPWATCH.md) for the instance
state and provider lifetime rules.
