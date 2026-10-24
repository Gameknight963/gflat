# Linux monotonic clock

Optional Linux LP64 source library. Add a project reference to `libraries/platform/linux/Linux.gfproj`; it uses the normal std reference. It is outside automatic std inclusion.

```csharp
alias Timer = std::Stopwatch<Linux::MonotonicClock>;
```

The provider calls libc `clock_gettime(CLOCK_MONOTONIC)` and exposes nanosecond units with frequency 1,000,000,000. Units do not imply nanosecond precision. Linux monotonic time excludes suspended time; consecutive readings may be equal. It is not a calendar timestamp. Invalid native results or signed-64-bit nanosecond overflow throw static-message exceptions.

This declaration supports Linux LP64 (64-bit pointers, C long and time_t), including the project's Linux x64 target. It does not support 32-bit Linux or x32. Include this optional project only for a supported Linux target. glibc 2.17 and newer provide clock_gettime through libc without adding librt.

Linux time namespaces reject offsets that would make the monotonic clock negative, so a negative seconds result is rejected. See the [time-namespace rules](https://man7.org/linux/man-pages/man7/time_namespaces.7.html).

Primary documentation: [clock_gettime manual](https://man7.org/linux/man-pages/man3/clock_gettime.3.html), [Linux clock IDs and timespec](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/linux/time.h), [Linux scalar types](https://raw.githubusercontent.com/torvalds/linux/master/include/uapi/asm-generic/posix_types.h), [glibc timespec](https://raw.githubusercontent.com/bminor/glibc/master/time/bits/types/struct_timespec.h).
