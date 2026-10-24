namespace Linux;

namespace system_clock_detail
{
    internal std::DateTime FromUnixTime(long seconds, long nanoseconds)
    {
        if (nanoseconds < 0l || nanoseconds >= 1000000000l)
            throw new Exception("clock_gettime returned an invalid timestamp");
        // Check before shifting epochs or multiplying, including negative Unix seconds.
        if (seconds < -62135596800l || seconds > 253402300799l)
            throw new Exception("System time is outside years 1 through 9999");
        long ticks = (seconds + 62135596800l) * 10000000l + nanoseconds / 100l;
        return std::DateTime::FromTicks(ticks);
    }
}

public static class SystemClock
{
    private static bool HasCompatibleLayout() nothrow
        => sizeof(nint) == 8 && sizeof(monotonic_clock_detail::Timespec) == 16;

    public static std::DateTime GetUtcNow()
    {
        if (!HasCompatibleLayout())
            throw new Exception("SystemClock requires Linux LP64");
        // Linux UAPI defines CLOCK_REALTIME as 0.
        monotonic_clock_detail::Timespec value = monotonic_clock_detail::Read(0);
        return system_clock_detail::FromUnixTime((long)value.Seconds, (long)value.Nanoseconds);
    }
}
