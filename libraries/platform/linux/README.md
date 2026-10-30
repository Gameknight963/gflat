# Linux clocks

Optional Linux LP64 source library. Add a project reference to `libraries/platform/linux/Linux.gfproj`; it uses the normal Standard reference. It is outside automatic Standard inclusion.

```csharp
using Standard::Time;
alias Timer = Standard::Time::Stopwatch<Linux::MonotonicClock>;
```

The provider calls libc `clock_gettime(CLOCK_MONOTONIC)` and exposes nanosecond units with frequency 1,000,000,000. Units do not imply nanosecond precision. Linux monotonic time excludes suspended time; consecutive readings may be equal. It is not a calendar timestamp. Invalid native results or signed-64-bit nanosecond overflow throw static-message exceptions.

This declaration supports Linux LP64 (64-bit pointers, C long and time_t), including the project's Linux x64 target. It does not support 32-bit Linux or x32. Include this optional project only for a supported Linux target. glibc 2.17 and newer provide clock_gettime through libc without adding librt.

Linux time namespaces reject offsets that would make the monotonic clock negative, so a negative seconds result is rejected. See the [time-namespace rules](https://man7.org/linux/man-pages/man7/time_namespaces.7.html).

Primary documentation: [clock_gettime manual](https://man7.org/linux/man-pages/man3/clock_gettime.3.html), [Linux clock IDs and timespec](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/linux/time.h), [Linux scalar types](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/asm-generic/posix_types.h), [glibc timespec](https://raw.githubusercontent.com/bminor/glibc/master/time/bits/types/struct_timespec.h).

## UTC wall-clock time

`Linux::SystemClock::GetUtcNow()` returns a `Standard::Time::DateTime` interpreted as UTC
by this method's contract. DateTime remains a civil value with no timezone or
UTC/local marker. This optional Linux project calls libc clock_gettime with
CLOCK_REALTIME; it does not add an implicit core clock or provider.

```csharp
using Standard::Time;
Standard::Time::DateTime utc = Linux::SystemClock::GetUtcNow();
utc.Format(Console::Out);
Console::WriteLine();
```

The method shares MonotonicClock's exact libc import and LP64 timespec type,
using the existing Linux x64/LP64 support and glibc 2.17+ linkage. Negative Unix
seconds are valid, including dates before 1970. Seconds and normalized nanoseconds
are range checked before conversion. Nanoseconds are truncated down to the
preceding 100ns boundary, including pre-epoch readings. Values outside Gregorian
years 1..9999 and invalid nanoseconds throw static-message Exceptions; native
clock_gettime errors propagate as the existing static-message Exception.

CLOCK_REALTIME is adjustable and need not be monotonic. Use
Stopwatch<Linux::MonotonicClock> for elapsed durations. Counter units do not
promise hardware accuracy. This API follows the host's Unix/system-clock leap
second behavior; DateTime has no second-60 representation. DateTime formatting
adds no Z/offset suffix. No local-time conversion, timezone rules, or
cross-host synchronization guarantee is supplied. Successful calls make no
application allocation.

Primary references: [clock_gettime](https://man7.org/linux/man-pages/man3/clock_gettime.3.html),
[Linux clock IDs](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/linux/time.h),
[glibc timespec layout](https://raw.githubusercontent.com/bminor/glibc/master/time/bits/types/struct_timespec.h).
