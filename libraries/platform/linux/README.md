# Linux clocks

Reference `libraries/platform/linux/Linux.gfproj` for Linux LP64 clock providers.
The project includes Standard and uses libc `clock_gettime`, available through
libc on glibc 2.17 and newer.

```csharp
using Standard::Time;
alias Timer = Standard::Time::Stopwatch<Linux::MonotonicClock>;
```

`Linux::MonotonicClock` measures intervals with `CLOCK_MONOTONIC`. Its frequency is
1,000,000,000 units per second. Actual clock precision depends on the host.
Suspended time is excluded, and consecutive readings can be equal. Invalid native
results or signed-64-bit nanosecond overflow throw an exception.

The declarations require 64-bit pointers, C `long`, and `time_t`, matching the
project's Linux x64 target. Linux time namespaces constrain monotonic offsets to
keep readings nonnegative. See [time namespaces](https://man7.org/linux/man-pages/man7/time_namespaces.7.html).

## UTC wall-clock time

`Linux::SystemClock::GetUtcNow()` returns a `Standard::Time::DateTime` representing
UTC, using `CLOCK_REALTIME`:

```csharp
using Standard::Time;
Standard::Time::DateTime utc = Linux::SystemClock::GetUtcNow();
utc.Format(Console::Out);
Console::WriteLine();
```

- Accepts negative Unix seconds for dates before 1970
- Checks seconds and normalized nanoseconds before conversion
- Truncates nanoseconds to the preceding 100ns boundary, including pre-epoch values
- Throws for invalid nanoseconds, years outside 1–9999, or native clock errors

`CLOCK_REALTIME` follows system clock adjustments. Use the monotonic provider for
elapsed durations. DateTime carries a civil date and time with seconds 0–59 and
leaves timezone labeling to the caller. Add a UTC marker when serializing these
values. Successful calls make no application allocation.

Native references: [clock_gettime](https://man7.org/linux/man-pages/man3/clock_gettime.3.html),
[Linux clock IDs](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/linux/time.h),
[LP64 scalar types](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/asm-generic/posix_types.h),
[glibc timespec](https://raw.githubusercontent.com/bminor/glibc/master/time/bits/types/struct_timespec.h).
