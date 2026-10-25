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
    public double TotalMilliseconds { immutable get nothrow => (double)ticks / 10000d; }
    public double TotalMinutes { immutable get nothrow => (double)ticks / 600000000d; }
    public double TotalHours { immutable get nothrow => (double)ticks / 36000000000d; }
    public double TotalDays { immutable get nothrow => (double)ticks / 864000000000d; }

    private static bool ReadTwoDigits(readonly(char)* text, nuint count, ref nuint position, ref int value) nothrow
    {
        if (count - position < 2) return false;
        char first = text[position];
        char second = text[position + 1];
        if (first < '0' || first > '9' || second < '0' || second > '9') return false;
        value = (first - '0') * 10 + second - '0';
        position += 2;
        return true;
    }

    // Internal parsing writes only the wrapper's local tick count. Its unsigned
    // magnitude includes the extra negative endpoint without negating MinValue.
    private static bool ReadInterval(readonly(char)* text, nuint count, ref long value) nothrow
    {
        nuint position = 0;
        bool negative = text[0] == '-';
        if (negative) position++;
        ulong limit = (ulong)MaximumTicks;
        if (negative) limit++;
        ulong dayLimit = limit / 864000000000ul;
        nuint first = position;
        ulong prefix = 0ul;
        while (position < count && text[position] >= '0' && text[position] <= '9')
        {
            ulong digit = (ulong)(text[position] - '0');
            if (prefix > (dayLimit - digit) / 10ul) return false;
            prefix = prefix * 10ul + digit;
            position++;
        }
        if (position == first || position == count) return false;
        ulong days = 0ul;
        int hours = 0;
        if (text[position] == '.')
        {
            days = prefix;
            position++;
            if (!ReadTwoDigits(text, count, ref position, ref hours)) return false;
        }
        else
        {
            if (position - first != 2) return false;
            hours = (int)prefix;
        }
        if (hours > 23 || position == count || text[position] != ':') return false;
        position++;
        int minutes = 0;
        if (!ReadTwoDigits(text, count, ref position, ref minutes) || minutes > 59
            || position == count || text[position] != ':') return false;
        position++;
        int seconds = 0;
        if (!ReadTwoDigits(text, count, ref position, ref seconds) || seconds > 59) return false;

        ulong fraction = 0ul;
        if (position < count)
        {
            if (text[position] != '.') return false;
            position++;
            nuint digits = count - position;
            if (digits == 0 || digits > 7) return false;
            for (nuint i = 0; i < digits; i++)
            {
                char digit = text[position++];
                if (digit < '0' || digit > '9') return false;
                fraction = fraction * 10ul + (ulong)(digit - '0');
            }
            for (nuint i = digits; i < 7; i++) fraction *= 10ul;
        }
        // The bounded day prefix plus one day's components fits unsigned long.
        ulong magnitude = days * 864000000000ul + (ulong)hours * 36000000000ul
            + (ulong)minutes * 600000000ul + (ulong)seconds * 10000000ul + fraction;
        if (magnitude > limit) return false;
        if (negative)
        {
            if (magnitude == (ulong)MaximumTicks + 1ul) value = MinimumTicks;
            else value = -(long)magnitude;
        }
        else value = (long)magnitude;
        return true;
    }

    // Complete counted ASCII input; result may overlap aligned input storage.
    public static bool TryParse(readonly(char)* text, nuint count, TimeSpan* result) nothrow
    {
        TimeSpan parsed = new();
        if (count == 0 || count > 26) { *result = parsed; return false; }
        long value = 0l;
        bool success = ReadInterval(text, count, ref value);
        if (success) parsed.ticks = value;
        *result = parsed;
        return success;
    }

    public static bool TryParse(readonly(char)* text, TimeSpan* result) nothrow
    {
        nuint count = 0;
        while (count < 27 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }

    public static TimeSpan Parse(readonly(char)* text, nuint count)
    {
        TimeSpan result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid time interval");
        return result;
    }

    public static TimeSpan Parse(readonly(char)* text)
    {
        TimeSpan result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid time interval");
        return result;
    }

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
