namespace Windows;

namespace system_clock_detail
{
    struct FileTime
    {
        public uint Low;
        public uint High;
    }
    private extern void GetSystemTimePreciseAsFileTime(FileTime* value);

    internal std::DateTime FromFileTime(uint low, uint high)
    {
        ulong value = ((ulong)high << 32) | (ulong)low;
        // FILETIME starts at 1601-01-01; DateTime starts at 0001-01-01.
        if (value > 2650467743999999999ul)
            throw new Exception("System time is outside years 1 through 9999");
        return std::DateTime::FromTicks((long)value + 504911232000000000l);
    }

    internal FileTime Read()
    {
        FileTime value = new();
        GetSystemTimePreciseAsFileTime(&value);
        return value;
    }
}

public static class SystemClock
{
    private static bool HasCompatibleLayout() nothrow
        => sizeof(nint) == 8 && sizeof(system_clock_detail::FileTime) == 8;

    public static std::DateTime GetUtcNow()
    {
        if (!HasCompatibleLayout())
            throw new Exception("SystemClock requires Windows 64-bit");
        system_clock_detail::FileTime value = system_clock_detail::Read();
        return system_clock_detail::FromFileTime(value.Low, value.High);
    }
}
