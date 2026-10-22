namespace std;

// Proleptic Gregorian calendar date, independent of clocks and time zones.
public struct Date : IEquatable<Date>, IComparable<Date>, IHashable, IFormattable
{
    private int dayNumber;
    private const int MaximumDayNumber = 3652058;

    public Date() nothrow { dayNumber = 0; }

    private static bool Leap(int year) nothrow
        => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static int MonthLength(int year, int month) nothrow
    {
        if (month == 2) { if (Leap(year)) return 29; return 28; }
        if (month == 4 || month == 6 || month == 9 || month == 11) return 30;
        return 31;
    }

    public static bool IsLeapYear(int year)
    {
        if (year < 1 || year > 9999) throw new Exception("Date year must be between 1 and 9999");
        return Leap(year);
    }

    public static int DaysInMonth(int year, int month)
    {
        if (year < 1 || year > 9999) throw new Exception("Date year must be between 1 and 9999");
        if (month < 1 || month > 12) throw new Exception("Date month must be between 1 and 12");
        return MonthLength(year, month);
    }

    public Date(int year, int month, int day)
    {
        int limit = DaysInMonth(year, month);
        if (day < 1 || day > limit) throw new Exception("Date day is outside the selected month");
        int previous = year - 1;
        dayNumber = previous * 365 + previous / 4 - previous / 100 + previous / 400 + day - 1;
        for (int i = 1; i < month; i++) dayNumber += MonthLength(year, i);
    }

    public static Date FromDayNumber(int number)
    {
        if (number < 0 || number > MaximumDayNumber) throw new Exception("Date day number is outside the supported range");
        Date result = new(); result.dayNumber = number; return result;
    }

    public int DayNumber { immutable get nothrow => dayNumber; }

    private immutable void GetParts(ref int year, ref int month, ref int day) nothrow
    {
        int remaining = dayNumber;
        int centuries400 = remaining / 146097; remaining %= 146097;
        int centuries = remaining / 36524; if (centuries == 4) centuries = 3;
        remaining -= centuries * 36524;
        int years4 = remaining / 1461; remaining %= 1461;
        int years = remaining / 365; if (years == 4) years = 3;
        remaining -= years * 365;
        year = centuries400 * 400 + centuries * 100 + years4 * 4 + years + 1;
        month = 1;
        while (remaining >= MonthLength(year, month))
        {
            remaining -= MonthLength(year, month); month++;
        }
        day = remaining + 1;
    }

    public int Year
    {
        immutable get nothrow { int year = 0; int month = 0; int day = 0; this.GetParts(ref year, ref month, ref day); return year; }
    }
    public int Month
    {
        immutable get nothrow { int year = 0; int month = 0; int day = 0; this.GetParts(ref year, ref month, ref day); return month; }
    }
    public int Day
    {
        immutable get nothrow { int year = 0; int month = 0; int day = 0; this.GetParts(ref year, ref month, ref day); return day; }
    }

    public immutable Date AddDays(int days)
    {
        long result = (long)dayNumber + (long)days;
        if (result < 0l || result > (long)MaximumDayNumber) throw new Exception("Date addition is outside the supported range");
        return Date::FromDayNumber((int)result);
    }

    // Calendar arithmetic clamps the day when the destination month is shorter.
    public immutable Date AddMonths(int months)
    {
        int year = 0; int month = 0; int day = 0;
        this.GetParts(ref year, ref month, ref day);
        long index = (long)(year - 1) * 12l + (long)(month - 1) + (long)months;
        if (index < 0l || index >= 119988l) throw new Exception("Date addition is outside the supported range");
        year = (int)(index / 12l) + 1;
        month = (int)(index % 12l) + 1;
        int limit = MonthLength(year, month);
        if (day > limit) day = limit;
        return new Date(year, month, day);
    }

    public immutable Date AddYears(int years)
    {
        // Any larger offset leaves the supported range, whatever the starting date.
        // Check before multiplying so even extreme int arguments cannot overflow.
        if (years < -9998 || years > 9998) throw new Exception("Date addition is outside the supported range");
        return this.AddMonths(years * 12);
    }

    public immutable bool Equals(immutable(Date)* other) nothrow => dayNumber == other.dayNumber;
    public immutable int CompareTo(immutable(Date)* other) nothrow => dayNumber - other.dayNumber;
    public immutable nuint GetHashCode() nothrow => (nuint)dayNumber;

    // Fixed invariant ISO spelling, counted bytes without a null terminator.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        if (capacity < 10) return false;
        int year = 0; int month = 0; int day = 0;
        this.GetParts(ref year, ref month, ref day);
        output[0] = (char)('0' + year / 1000);
        output[1] = (char)('0' + year / 100 % 10);
        output[2] = (char)('0' + year / 10 % 10);
        output[3] = (char)('0' + year % 10);
        output[4] = '-';
        output[5] = (char)('0' + month / 10); output[6] = (char)('0' + month % 10);
        output[7] = '-';
        output[8] = (char)('0' + day / 10); output[9] = (char)('0' + day % 10);
        written = 10; return true;
    }

    public immutable void Format(ITextWriter* output)
    {
        char[10] buffer; nuint written = 0;
        this.TryFormat(buffer, 10, ref written);
        output.Write(buffer, written);
    }
}
