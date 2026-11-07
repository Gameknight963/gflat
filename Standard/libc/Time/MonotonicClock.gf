namespace Standard::Time;

// System elapsed-time counter. Its epoch is unrelated to calendar or Unix time.
public static class MonotonicClock : IMonotonicClock
{
    public static long GetFrequency()
    {
        long value = 0l;
        if (monotonic_clock_detail::__gflat_stdio_monotonic_frequency(ref value) == 0 || value <= 0l)
            throw new Exception("Monotonic clock frequency query failed");
        return value;
    }

    public static long GetTimestamp()
    {
        long value = 0l;
        if (monotonic_clock_detail::__gflat_stdio_monotonic_timestamp(ref value) == 0)
            throw new Exception("Monotonic clock timestamp query failed");
        return value;
    }
}

namespace monotonic_clock_detail
{
    private extern "C" int __gflat_stdio_monotonic_frequency(ref long value) nothrow;
    private extern "C" int __gflat_stdio_monotonic_timestamp(ref long value) nothrow;
}
