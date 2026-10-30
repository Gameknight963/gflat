using Standard::Time;
namespace Linux;

namespace monotonic_clock_detail
{
    // Linux LP64 libc timespec: time_t seconds followed by long nanoseconds.
    struct Timespec
    {
        public nint Seconds;
        public nint Nanoseconds;
    }
    private extern int clock_gettime(int clock, Timespec* result);

    // Share this exact native signature and timespec identity with SystemClock.
    internal Timespec Read(int clock)
    {
        Timespec value = new();
        if (clock_gettime(clock, &value) != 0)
            throw new Exception("clock_gettime failed");
        return value;
    }
}

public static class MonotonicClock : Standard::Time::IMonotonicClock
{
    private static bool HasCompatibleLayout() nothrow
        => sizeof(nint) == 8 && sizeof(monotonic_clock_detail::Timespec) == 16;

    public static long GetFrequency() nothrow => 1000000000l;

    public static long GetTimestamp()
    {
        if (!HasCompatibleLayout())
            throw new Exception("MonotonicClock requires Linux LP64");
        // Linux UAPI defines CLOCK_MONOTONIC as 1.
        monotonic_clock_detail::Timespec value = monotonic_clock_detail::Read(1);
        long seconds = (long)value.Seconds;
        long nanoseconds = (long)value.Nanoseconds;
        if (seconds < 0l || nanoseconds < 0l || nanoseconds >= 1000000000l)
            throw new Exception("clock_gettime returned an invalid timestamp");
        if (seconds > (9223372036854775807l - nanoseconds) / 1000000000l)
            throw new Exception("Monotonic timestamp exceeds the signed 64-bit range");
        return seconds * 1000000000l + nanoseconds;
    }
}
