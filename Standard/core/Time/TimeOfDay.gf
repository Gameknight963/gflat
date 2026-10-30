namespace Standard::Time;

// Time within one day, in 100 ns ticks. No date or time zone is attached.
public struct TimeOfDay : IEquatable<TimeOfDay>, IComparable<TimeOfDay>, IHashable, IFormattable
{
    private long ticks;
    private static const long TicksPerDay = 864000000000l;

    public TimeOfDay() nothrow { ticks = 0l; }
    public TimeOfDay(int hour, int minute, int second = 0, int millisecond = 0)
    {
        ticks = 0l;
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59
            || second < 0 || second > 59 || millisecond < 0 || millisecond > 999)
            throw new Exception("TimeOfDay component is out of range");
        ticks = (long)hour * 36000000000l + (long)minute * 600000000l
            + (long)second * 10000000l + (long)millisecond * 10000l;
    }
    public static TimeOfDay FromTicks(long value)
    {
        if (value < 0l || value >= TicksPerDay) throw new Exception("TimeOfDay ticks are outside one day");
        TimeOfDay result = new(); result.ticks = value; return result;
    }
    public long Ticks { immutable get nothrow => ticks; }
    public int Hour { immutable get nothrow => (int)(ticks / 36000000000l); }
    public int Minute { immutable get nothrow => (int)(ticks / 600000000l % 60l); }
    public int Second { immutable get nothrow => (int)(ticks / 10000000l % 60l); }
    public int Millisecond { immutable get nothrow => (int)(ticks / 10000l % 1000l); }

    public immutable TimeOfDay Add(TimeSpan value) nothrow
    {
        int wrappedDays = 0;
        return this.Add(value, ref wrappedDays);
    }
    public immutable TimeOfDay Add(TimeSpan value, ref int wrappedDays) nothrow
    {
        long delta = value.Ticks;
        long days = delta / TicksPerDay;
        // Remainder magnitude is less than a day, avoiding ticks+delta overflow.
        long shifted = ticks + delta % TicksPerDay;
        if (shifted < 0l) { shifted += TicksPerDay; days--; }
        else if (shifted >= TicksPerDay) { shifted -= TicksPerDay; days++; }
        TimeOfDay result = new(); result.ticks = shifted;
        wrappedDays = (int)days;
        return result;
    }
    public immutable bool IsBetween(TimeOfDay start, TimeOfDay end) nothrow
    {
        if (start.ticks <= end.ticks) return ticks >= start.ticks && ticks < end.ticks;
        return ticks >= start.ticks || ticks < end.ticks;
    }

    public static bool TryParse(readonly(char)* text, nuint count, TimeOfDay* result) nothrow
    {
        TimeOfDay parsed = new();
        if (count < 8 || count > 16) { *result = parsed; return false; }
        if (text[2] != ':' || text[5] != ':' || text[0] < '0' || text[0] > '9'
            || text[1] < '0' || text[1] > '9') { *result = parsed; return false; }
        TimeSpan interval = new();
        if (!TimeSpan::TryParse(text, count, &interval) || interval.Ticks < 0l || interval.Ticks >= TicksPerDay)
        { *result = parsed; return false; }
        parsed.ticks = interval.Ticks; *result = parsed; return true;
    }
    public static bool TryParse(readonly(char)* text, TimeOfDay* result) nothrow
    {
        nuint count = 0;
        while (count < 17 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }
    public static TimeOfDay Parse(readonly(char)* text, nuint count)
    {
        TimeOfDay result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid time of day");
        return result;
    }
    public static TimeOfDay Parse(readonly(char)* text)
    {
        TimeOfDay result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid time of day");
        return result;
    }
    public immutable bool Equals(immutable(TimeOfDay)* other) nothrow => ticks == other.ticks;
    public immutable int CompareTo(immutable(TimeOfDay)* other) nothrow
    {
        if (ticks < other.ticks) return -1;
        if (ticks > other.ticks) return 1;
        return 0;
    }
    public immutable nuint GetHashCode() nothrow => Hashing::Integer((ulong)ticks);
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        TimeSpan interval = TimeSpan::FromTicks(ticks);
        return interval.TryFormat(output, capacity, ref written);
    }
    public immutable void Format(ITextWriter* output)
    {
        char[16] buffer; nuint written = 0;
        this.TryFormat(buffer, 16, ref written);
        output.Write(buffer, written);
    }
}
