using std::Time;
namespace Windows;

namespace performance_counter_detail
{
    // Win64 BOOL is int; LARGE_INTEGER storage is a signed 64-bit integer.
    private extern int QueryPerformanceCounter(long* value);
    private extern int QueryPerformanceFrequency(long* value);
}

public static class PerformanceCounter : std::Time::IMonotonicClock
{
    private static bool HasCompatibleLayout() nothrow => sizeof(nint) == 8;

    public static long GetTimestamp()
    {
        if (!HasCompatibleLayout()) throw new Exception("PerformanceCounter requires Windows 64-bit");
        long value = 0l;
        if (performance_counter_detail::QueryPerformanceCounter(&value) == 0)
            throw new Exception("QueryPerformanceCounter failed");
        return value;
    }

    public static long GetFrequency()
    {
        if (!HasCompatibleLayout()) throw new Exception("PerformanceCounter requires Windows 64-bit");
        long value = 0l;
        if (performance_counter_detail::QueryPerformanceFrequency(&value) == 0 || value <= 0l)
            throw new Exception("QueryPerformanceFrequency failed");
        return value;
    }
}
