# Measuring system elapsed time

`Standard::Time::MonotonicClock` supplies the system counter for
`Stopwatch<MonotonicClock>`. It implements the same static `IMonotonicClock`
contract as caller-supplied test clocks.

```csharp
using Standard::Time;

int main()
{
    long start = MonotonicClock::GetTimestamp();
    var timer = Stopwatch<MonotonicClock>::StartNew();
    // Perform work here.
    timer.Stop();
    long end = MonotonicClock::GetTimestamp();
    TimeSpan elapsed = Stopwatch<MonotonicClock>::GetElapsedTime(start, end);
    if (timer.IsRunning || timer.ElapsedTicks < 0l || elapsed.Ticks < 0l) return 1;
    return 0;
}
```

`GetTimestamp()` returns a signed 64-bit counter sample. `GetFrequency()` returns
its fixed positive units per second. Use samples from this provider together,
and convert intervals using that frequency or Stopwatch's interval helpers.
Counter ticks need not be TimeSpan's 100-nanosecond ticks.

The counter has an arbitrary origin and is for elapsed time rather than calendar
or Unix time. Timestamp ordering does not move backwards when wall-clock settings
change. The provider uses QueryPerformanceCounter on Windows and CLOCK_MONOTONIC
on POSIX hosts. Windows reports its performance-counter frequency; POSIX samples
are represented as nanoseconds with a frequency of 1,000,000,000 per second.

Suspend behavior is platform-specific. Windows' counter includes machine sleep,
while Linux CLOCK_MONOTONIC excludes suspended time. See the
[Windows counter guidance](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps)
and [Linux clock documentation](https://man7.org/linux/man-pages/man2/clock_gettime.2.html).

## Dependencies and failures

Successful counter and frequency queries do not allocate Standard-owned storage.
An OS query failure or an unrepresentable timestamp throws `Exception`. The
counter's resolution is platform-dependent and need not equal one frequency unit.

Load `libc/Time/MonotonicClock.gf` with `core/Time/IMonotonicClock.gf` and the
ordinary exception support. Stopwatch additionally needs `core/Time/Stopwatch.gf`
and `core/Time/TimeSpan.gf` with their formatting dependencies. The system provider
uses the existing native IO bridge. Pure core programs can continue to select
an application-defined clock without loading the system provider.
