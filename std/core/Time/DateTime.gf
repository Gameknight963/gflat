namespace std::Time;

// Gregorian civil date and wall-clock time. No time zone, offset, or instant is implied.
public struct DateTime : IEquatable<DateTime>, IComparable<DateTime>, IHashable, IFormattable
{
    private Date date;
    private TimeOfDay time;
    private static const long TicksPerDay = 864000000000l;
    private static const long MaximumTicks = 3155378975999999999l;

    public DateTime() nothrow { date = new(); time = new(); }
    public DateTime(Date date, TimeOfDay time) nothrow { this.date = date; this.time = time; }

    public Date Date { immutable get nothrow => date; }
    public TimeOfDay TimeOfDay { immutable get nothrow => time; }
    // Ordinal ticks since 0001-01-01 at midnight in this civil calendar.
    public long Ticks { immutable get nothrow => (long)date.DayNumber * TicksPerDay + time.Ticks; }

    public static DateTime FromTicks(long value)
    {
        if (value < 0l || value > MaximumTicks) throw new Exception("DateTime ticks are outside years 1 through 9999");
        Date day = Date::FromDayNumber((int)(value / TicksPerDay));
        TimeOfDay clock = TimeOfDay::FromTicks(value % TicksPerDay);
        return new DateTime(day, clock);
    }

    public immutable DateTime Add(TimeSpan value)
    {
        long current = this.Ticks;
        long delta = value.Ticks;
        if (delta < -current || delta > MaximumTicks - current)
            throw new Exception("DateTime addition is outside years 1 through 9999");
        return FromTicks(current + delta);
    }

    /// Returns this civil date and time minus the signed duration, rejecting results outside years 1 through 9999.
    public immutable DateTime Subtract(TimeSpan value)
    {
        long current = this.Ticks;
        long delta = value.Ticks;
        // Do not negate delta: TimeSpan can contain long minimum.
        if (delta > current || delta < current - MaximumTicks)
            throw new Exception("DateTime subtraction is outside years 1 through 9999");
        return FromTicks(current - delta);
    }

    public immutable TimeSpan Difference(DateTime other) nothrow
        => TimeSpan::FromTicks(this.Ticks - other.Ticks);

    public immutable DateTime AddMonths(int months) => new DateTime(date.AddMonths(months), time);
    public immutable DateTime AddYears(int years) => new DateTime(date.AddYears(years), time);

    public immutable bool Equals(immutable(DateTime)* other) nothrow => this.Ticks == other.Ticks;
    public immutable int CompareTo(immutable(DateTime)* other) nothrow
    {
        long first = this.Ticks;
        long second = other.Ticks;
        if (first < second) return -1;
        if (first > second) return 1;
        return 0;
    }
    public immutable nuint GetHashCode() nothrow => Hashing::Integer((ulong)this.Ticks);

    // Exact yyyy-MM-ddTHH:mm:ss[.1..7 digits], with no offset/zone suffix.
    // All reads finish before the result store, so aligned result/input overlap is valid.
    public static bool TryParse(readonly(char)* text, nuint count, DateTime* result) nothrow
    {
        DateTime parsed = new();
        if (count < 19 || count > 27) { *result = parsed; return false; }
        if (text[10] != 'T') { *result = parsed; return false; }
        Date day = new();
        TimeOfDay clock = new();
        if (!Date::TryParse(text, 10, &day) || !TimeOfDay::TryParse(text + 11, count - 11, &clock))
        { *result = parsed; return false; }
        parsed = new DateTime(day, clock);
        *result = parsed;
        return true;
    }
    public static bool TryParse(readonly(char)* text, DateTime* result) nothrow
    {
        nuint count = 0;
        while (count < 28 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }
    public static DateTime Parse(readonly(char)* text, nuint count)
    {
        DateTime result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid civil date and time");
        return result;
    }
    public static DateTime Parse(readonly(char)* text)
    {
        DateTime result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid civil date and time");
        return result;
    }

    // Canonical date/time, no NUL: 19 bytes or 27 with a nonzero seven-digit fraction.
    // Output may overlap sufficiently backed receiver storage. Written must be separate.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        Date day = date;
        TimeOfDay clock = time;
        written = 0;
        char[27] buffer;
        nuint dateLength = 0;
        nuint timeLength = 0;
        day.TryFormat(buffer, 10, ref dateLength);
        buffer[10] = 'T';
        clock.TryFormat((char*)buffer + 11, 16, ref timeLength);
        nuint required = 11 + timeLength;
        if (capacity < required) return false;
        for (nuint i = 0; i < required; i++) output[i] = buffer[i];
        written = required;
        return true;
    }
    public immutable void Format(ITextWriter* output)
    {
        char[27] buffer;
        nuint written = 0;
        this.TryFormat(buffer, 27, ref written);
        output.Write(buffer, written);
    }
}
