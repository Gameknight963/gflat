namespace std;

// Proleptic Gregorian calendar date, independent of clocks and time zones.
public struct Date : IEquatable<Date>, IComparable<Date>, IHashable, IFormattable
{
    private int dayNumber;
    private static const int MaximumDayNumber = 3652058;

    public Date() nothrow { dayNumber = 0; }

    private static bool Leap(int year) nothrow
        => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static int MonthLength(int year, int month) nothrow
    {
        if (month == 2) { if (Leap(year)) return 29; return 28; }
        if (month == 4 || month == 6 || month == 9 || month == 11) return 30;
        return 31;
    }

    // Components must already have passed Gregorian range validation.
    private static int NumberFromParts(int year, int month, int day) nothrow
    {
        int previous = year - 1;
        int result = previous * 365 + previous / 4 - previous / 100 + previous / 400 + day - 1;
        for (int i = 1; i < month; i++) result += MonthLength(year, i);
        return result;
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
        dayNumber = NumberFromParts(year, month, day);
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

    public DayOfWeek DayOfWeek
    {
        immutable get nothrow => (DayOfWeek)((dayNumber + 1) % 7);
    }
    public int DayOfYear
    {
        immutable get nothrow
        {
            int year = 0; int month = 0; int day = 0;
            this.GetParts(ref year, ref month, ref day);
            return dayNumber - NumberFromParts(year, 1, 1) + 1;
        }
    }

    // Monday of the ISO year's first week: the week containing January 4.
    // Internal year 10000 is used only for the upper boundary calculation;
    // its day-number arithmetic still fits int and creates no Date value.
    private static int IsoWeekStart(int year) nothrow
    {
        int january4 = NumberFromParts(year, 1, 4);
        return january4 - january4 % 7;
    }

    private immutable int GetIsoYear() nothrow
    {
        int year = 0; int month = 0; int day = 0;
        this.GetParts(ref year, ref month, ref day);
        if (dayNumber < IsoWeekStart(year)) return year - 1;
        if (dayNumber >= IsoWeekStart(year + 1)) return year + 1;
        return year;
    }

    public int IsoWeekYear { immutable get nothrow => this.GetIsoYear(); }
    public int IsoWeekOfYear
    {
        immutable get nothrow => (dayNumber - IsoWeekStart(this.GetIsoYear())) / 7 + 1;
    }

    public static int GetIsoWeeksInYear(int year)
    {
        // Reuse Gregorian year validation; ISO years have the same public range.
        IsLeapYear(year);
        return (IsoWeekStart(year + 1) - IsoWeekStart(year)) / 7;
    }

    public static Date FromIsoWeek(int year, int week, DayOfWeek weekday)
    {
        int weeks = GetIsoWeeksInYear(year);
        if (week < 1 || week > weeks) throw new Exception("Date ISO week is outside the selected year");
        int day = (int)weekday;
        if (day < 0 || day > 6) throw new Exception("Date weekday must be between Sunday and Saturday");
        int offset = day - 1;
        if (day == 0) offset = 6;
        return FromDayNumber(IsoWeekStart(year) + (week - 1) * 7 + offset);
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

    // Strict invariant yyyy-MM-dd. Commit the result only after reading input,
    // so properly aligned result storage may overlap the input bytes.
    public static bool TryParse(readonly(char)* text, nuint count, Date* result) nothrow
    {
        Date parsed = new();
        if (count != 10) { *result = parsed; return false; }
        int year = 0; int month = 0; int day = 0;
        for (nuint i = 0; i < 10; i++)
        {
            char value = text[i];
            if (i == 4 || i == 7)
            {
                if (value != '-') { *result = parsed; return false; }
            }
            else
            {
                if (value < '0' || value > '9') { *result = parsed; return false; }
                int digit = value - '0';
                if (i < 4) year = year * 10 + digit;
                else if (i < 7) month = month * 10 + digit;
                else day = day * 10 + digit;
            }
        }
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > MonthLength(year, month))
        { *result = parsed; return false; }
        parsed.dayNumber = NumberFromParts(year, month, day);
        *result = parsed;
        return true;
    }

    public static bool TryParse(readonly(char)* text, Date* result) nothrow
    {
        nuint count = 0;
        while (count < 11 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }

    public static Date Parse(readonly(char)* text, nuint count)
    {
        Date result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid date; expected yyyy-MM-dd");
        return result;
    }

    public static Date Parse(readonly(char)* text)
    {
        Date result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid date; expected yyyy-MM-dd");
        return result;
    }

    public immutable bool Equals(immutable(Date)* other) nothrow => dayNumber == other.dayNumber;
    public immutable int CompareTo(immutable(Date)* other) nothrow => dayNumber - other.dayNumber;
    public immutable nuint GetHashCode() nothrow => (nuint)dayNumber;

    // Fixed invariant ISO spelling, counted bytes without a null terminator.
    // written must be separate from output and this value's storage. Components
    // are captured before output writes, so sufficiently backed output may overlap this value.
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
