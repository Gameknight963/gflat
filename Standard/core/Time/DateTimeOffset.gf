namespace Standard::Time;

// Civil wall time plus a whole-minute UTC offset; no named time zone is retained.
public struct DateTimeOffset : IEquatable<DateTimeOffset>, IComparable<DateTimeOffset>, IHashable, IFormattable
{
    private Standard::Time::DateTime clock;
    private long offsetTicks;
    private static const long MinuteTicks = 600000000l;
    private static const long MaximumOffset = 504000000000l;
    private static const long MaximumTicks = 3155378975999999999l;

    public DateTimeOffset() nothrow { clock = new(); offsetTicks = 0l; }
    private static bool ValidOffset(long offset) nothrow
        => offset >= -MaximumOffset && offset <= MaximumOffset && offset % MinuteTicks == 0l;
    private static bool Valid(Standard::Time::DateTime value, long offset) nothrow
    {
        if (!ValidOffset(offset)) return false;
        long utc = value.Ticks - offset;
        return utc >= 0l && utc <= MaximumTicks;
    }
    private static DateTimeOffset Create(Standard::Time::DateTime value, long offset) nothrow
    {
        DateTimeOffset result = new(); result.clock = value; result.offsetTicks = offset; return result;
    }
    public DateTimeOffset(Standard::Time::DateTime value, TimeSpan offset)
    {
        clock = new(); offsetTicks = 0l;
        if (!Valid(value, offset.Ticks)) throw new Exception("DateTimeOffset clock or offset is out of range");
        clock = value; offsetTicks = offset.Ticks;
    }
    private immutable long GetUtcTicks() nothrow => clock.Ticks - offsetTicks;
    public Standard::Time::DateTime DateTime { immutable get nothrow => clock; }
    public Standard::Time::DateTime UtcDateTime { immutable get => Standard::Time::DateTime::FromTicks(this.GetUtcTicks()); }
    public TimeSpan Offset { immutable get nothrow => TimeSpan::FromTicks(offsetTicks); }

    public immutable DateTimeOffset ToOffset(TimeSpan offset)
    {
        long selected = offset.Ticks;
        if (!ValidOffset(selected)) throw new Exception("DateTimeOffset clock or offset is out of range");
        long ticks = this.GetUtcTicks() + selected;
        if (ticks < 0l || ticks > MaximumTicks) throw new Exception("DateTimeOffset clock or offset is out of range");
        return Create(Standard::Time::DateTime::FromTicks(ticks), selected);
    }
    public static DateTimeOffset FromUnixTimeSeconds(long seconds)
    {
        if (seconds < -62135596800l || seconds > 253402300799l)
            throw new Exception("Unix seconds are outside years 1 through 9999");
        return Create(Standard::Time::DateTime::FromTicks((seconds + 62135596800l) * 10000000l), 0l);
    }
    public static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds)
    {
        if (milliseconds < -62135596800000l || milliseconds > 253402300799999l)
            throw new Exception("Unix milliseconds are outside years 1 through 9999");
        return Create(Standard::Time::DateTime::FromTicks((milliseconds + 62135596800000l) * 10000l), 0l);
    }
    // UTC ticks are nonnegative: divide before shifting epoch to floor negatives.
    public immutable long ToUnixTimeSeconds() nothrow => this.GetUtcTicks() / 10000000l - 62135596800l;
    public immutable long ToUnixTimeMilliseconds() nothrow => this.GetUtcTicks() / 10000l - 62135596800000l;

    // Preserve the displayed offset; both wall and UTC clocks must remain valid.
    public immutable DateTimeOffset Add(TimeSpan value)
    {
        long delta = value.Ticks;
        long wall = clock.Ticks;
        long utc = this.GetUtcTicks();
        if (delta < -wall || delta > MaximumTicks - wall ||
            delta < -utc || delta > MaximumTicks - utc)
            throw new Exception("DateTimeOffset addition is outside years 1 through 9999");
        return Create(Standard::Time::DateTime::FromTicks(wall + delta), offsetTicks);
    }
    public immutable DateTimeOffset Subtract(TimeSpan value)
    {
        long delta = value.Ticks;
        long wall = clock.Ticks;
        long utc = this.GetUtcTicks();
        // Do not negate delta: TimeSpan can contain long minimum.
        if (delta > wall || delta < wall - MaximumTicks ||
            delta > utc || delta < utc - MaximumTicks)
            throw new Exception("DateTimeOffset subtraction is outside years 1 through 9999");
        return Create(Standard::Time::DateTime::FromTicks(wall - delta), offsetTicks);
    }
    /// Shift the civil calendar, clamping the day and preserving time and offset; both wall and UTC results must remain valid.
    public immutable DateTimeOffset AddMonths(int months)
        => new DateTimeOffset(clock.AddMonths(months), TimeSpan::FromTicks(offsetTicks));
    /// Shift the civil year with the same clamping, time, offset and range rules as AddMonths.
    public immutable DateTimeOffset AddYears(int years)
        => new DateTimeOffset(clock.AddYears(years), TimeSpan::FromTicks(offsetTicks));

    public immutable TimeSpan Difference(DateTimeOffset other) nothrow
        => TimeSpan::FromTicks(this.GetUtcTicks() - other.GetUtcTicks());
    public immutable bool Equals(immutable(DateTimeOffset)* other) nothrow => this.GetUtcTicks() == other.GetUtcTicks();
    public immutable bool EqualsExact(immutable(DateTimeOffset)* other) nothrow
        => clock.Ticks == other.clock.Ticks && offsetTicks == other.offsetTicks;
    public immutable int CompareTo(immutable(DateTimeOffset)* other) nothrow
    {
        long first = this.GetUtcTicks(); long second = other.GetUtcTicks();
        if (first < second) return -1; if (first > second) return 1; return 0;
    }
    public immutable nuint GetHashCode() nothrow => Hashing::Integer((ulong)this.GetUtcTicks());

    private static int TwoDigits(readonly(char)* text) nothrow
    {
        if (text[0] < '0' || text[0] > '9' || text[1] < '0' || text[1] > '9') return -1;
        return (text[0] - '0') * 10 + text[1] - '0';
    }
    private static bool TryRead(readonly(char)* text, nuint count, ref DateTimeOffset parsed) nothrow
    {
        if (count < 20 || count > 33) return false;
        nuint prefix = count - 1; long offset = 0l;
        if (text[prefix] != 'Z')
        {
            if (count < 25) return false;
            prefix = count - 6;
            if ((text[prefix] != '+' && text[prefix] != '-') || text[prefix + 3] != ':') return false;
            int hours = TwoDigits(text + prefix + 1); int minutes = TwoDigits(text + prefix + 4);
            if (hours < 0 || hours > 14 || minutes < 0 || minutes > 59) return false;
            offset = (long)(hours * 60 + minutes) * MinuteTicks;
            if (text[prefix] == '-') offset = -offset;
        }
        Standard::Time::DateTime value = new();
        if (!Standard::Time::DateTime::TryParse(text, prefix, &value) || !Valid(value, offset)) return false;
        parsed = Create(value, offset); return true;
    }
    // Candidate reads complete before output is written, including failure reset.
    public static bool TryParse(readonly(char)* text, nuint count, DateTimeOffset* result) nothrow
    {
        DateTimeOffset parsed = new(); bool success = TryRead(text, count, ref parsed); *result = parsed; return success;
    }
    public static bool TryParse(readonly(char)* text, DateTimeOffset* result) nothrow
    {
        nuint count = 0; while (count < 34 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }
    public static DateTimeOffset Parse(readonly(char)* text, nuint count)
    {
        DateTimeOffset result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid date and time offset"); return result;
    }
    public static DateTimeOffset Parse(readonly(char)* text)
    {
        DateTimeOffset result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid date and time offset"); return result;
    }

    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0; char[33] staged; nuint length = 0; clock.TryFormat(staged, 27, ref length);
        long offset = offsetTicks;
        if (offset == 0l) { staged[length] = 'Z'; length++; }
        else
        {
            staged[length] = '+'; if (offset < 0l) { staged[length] = '-'; offset = -offset; } length++;
            int minutes = (int)(offset / MinuteTicks); int hours = minutes / 60; minutes %= 60;
            staged[length++] = (char)('0' + hours / 10); staged[length++] = (char)('0' + hours % 10);
            staged[length++] = ':'; staged[length++] = (char)('0' + minutes / 10); staged[length++] = (char)('0' + minutes % 10);
        }
        if (capacity < length) return false;
        for (nuint i = 0; i < length; i++) output[i] = staged[i]; written = length; return true;
    }
    public immutable void Format(ITextWriter* output)
    {
        char[33] buffer; nuint written = 0; this.TryFormat(buffer, 33, ref written); output.Write(buffer, written);
    }
}
