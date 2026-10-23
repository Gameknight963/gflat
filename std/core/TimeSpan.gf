namespace std;

// Signed duration in 100 ns ticks, independent of calendars and clocks.
public struct TimeSpan : IEquatable<TimeSpan>, IComparable<TimeSpan>, IHashable, IFormattable
{
    private long ticks;
    private static const long MinimumTicks = -9223372036854775807l - 1l;
    private static const long MaximumTicks = 9223372036854775807l;

    public TimeSpan() nothrow { ticks = 0l; }

    public static TimeSpan FromTicks(long value) nothrow
    {
        TimeSpan result = new(); result.ticks = value; return result;
    }

    // All scales are positive, so truncation toward zero gives both valid bounds.
    private static TimeSpan FromScaled(long value, long scale)
    {
        if (value < MinimumTicks / scale || value > MaximumTicks / scale)
            throw new Exception("TimeSpan factory overflow");
        return FromTicks(value * scale);
    }

    public static TimeSpan FromMilliseconds(long value) => FromScaled(value, 10000l);
    public static TimeSpan FromSeconds(long value) => FromScaled(value, 10000000l);
    public static TimeSpan FromMinutes(long value) => FromScaled(value, 600000000l);
    public static TimeSpan FromHours(long value) => FromScaled(value, 36000000000l);
    public static TimeSpan FromDays(long value) => FromScaled(value, 864000000000l);

    public long Ticks { immutable get nothrow => ticks; }
    public int Days { immutable get nothrow => (int)(ticks / 864000000000l); }
    public int Hours { immutable get nothrow => (int)(ticks / 36000000000l % 24l); }
    public int Minutes { immutable get nothrow => (int)(ticks / 600000000l % 60l); }
    public int Seconds { immutable get nothrow => (int)(ticks / 10000000l % 60l); }
    public int Milliseconds { immutable get nothrow => (int)(ticks / 10000l % 1000l); }
    public double TotalSeconds { immutable get nothrow => (double)ticks / 10000000d; }

    public immutable TimeSpan Add(TimeSpan other)
    {
        if ((other.ticks > 0l && ticks > MaximumTicks - other.ticks)
            || (other.ticks < 0l && ticks < MinimumTicks - other.ticks))
            throw new Exception("TimeSpan addition overflow");
        return FromTicks(ticks + other.ticks);
    }

    public immutable TimeSpan Subtract(TimeSpan other)
    {
        if ((other.ticks > 0l && ticks < MinimumTicks + other.ticks)
            || (other.ticks < 0l && ticks > MaximumTicks + other.ticks))
            throw new Exception("TimeSpan subtraction overflow");
        return FromTicks(ticks - other.ticks);
    }

    public immutable TimeSpan Negate()
    {
        if (ticks == MinimumTicks) throw new Exception("TimeSpan negation overflow");
        return FromTicks(-ticks);
    }

    public immutable bool Equals(immutable(TimeSpan)* other) nothrow => ticks == other.ticks;
    public immutable int CompareTo(immutable(TimeSpan)* other) nothrow
    {
        if (ticks < other.ticks) return -1;
        if (ticks > other.ticks) return 1;
        return 0;
    }
    public immutable nuint GetHashCode() nothrow => Hashing::Integer((ulong)ticks);

    private static void WriteDigits(char* output, nuint count, ulong value) nothrow
    {
        while (count != 0)
        {
            count--;
            output[count] = (char)('0' + (int)(value % 10ul));
            value /= 10ul;
        }
    }

    // Invariant [-][d.]hh:mm:ss[.fffffff], counted bytes without a terminator.
    // Output may overlap this value: prepare all bytes before writing output.
    // written must be separate from output and this value's storage.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        long value = ticks;
        written = 0;
        char[26] buffer;
        nuint count = 0;
        ulong magnitude = (ulong)value;
        if (value < 0l) { buffer[count++] = '-'; magnitude = 0ul - magnitude; }
        ulong days = magnitude / 864000000000ul;
        if (days != 0ul)
        {
            nuint digits = 1;
            for (ulong rest = days / 10ul; rest != 0ul; rest /= 10ul) digits++;
            WriteDigits((char*)buffer + count, digits, days);
            count += digits;
            buffer[count++] = '.';
        }
        WriteDigits((char*)buffer + count, 2, magnitude / 36000000000ul % 24ul); count += 2;
        buffer[count++] = ':';
        WriteDigits((char*)buffer + count, 2, magnitude / 600000000ul % 60ul); count += 2;
        buffer[count++] = ':';
        WriteDigits((char*)buffer + count, 2, magnitude / 10000000ul % 60ul); count += 2;
        ulong fraction = magnitude % 10000000ul;
        if (fraction != 0ul)
        {
            buffer[count++] = '.';
            WriteDigits((char*)buffer + count, 7, fraction); count += 7;
        }
        if (capacity < count) return false;
        for (nuint i = 0; i < count; i++) output[i] = buffer[i];
        written = count;
        return true;
    }

    public immutable void Format(ITextWriter* output)
    {
        char[26] buffer; nuint written = 0;
        this.TryFormat(buffer, 26, ref written);
        output.Write(buffer, written);
    }
}
