namespace std;

// Invariant decimal text into caller storage, without allocation or a terminator.
// The written-count storage must be disjoint from output. Failure writes zero
// to written and leaves the character buffer untouched.
public static class IntegerFormatting
{
    private static bool Magnitude(ulong value, bool negative, char* output,
        nuint capacity, ref nuint written) nothrow
    {
        char[21] digits;
        nuint start = 21;
        while (true)
        {
            start--;
            digits[start] = (char)(48ul + value % 10ul);
            value /= 10ul;
            if (value == 0ul) break;
        }
        if (negative) { start--; digits[start] = '-'; }
        nuint length = 21 - start;
        written = 0;
        if (capacity < length) return false;
        for (nuint i = 0; i < length; i++) output[i] = digits[start + i];
        written = length;
        return true;
    }

    public static bool TryFormat(ulong value, char* output, nuint capacity, ref nuint written) nothrow
        => Magnitude(value, false, output, capacity, ref written);

    public static bool TryFormat(long value, char* output, nuint capacity, ref nuint written) nothrow
    {
        if (value < 0l) return Magnitude(0ul - (ulong)value, true, output, capacity, ref written);
        return Magnitude((ulong)value, false, output, capacity, ref written);
    }

    public static bool TryFormat(sbyte value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((long)value, output, capacity, ref written);
    public static bool TryFormat(byte value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((ulong)value, output, capacity, ref written);
    public static bool TryFormat(short value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((long)value, output, capacity, ref written);
    public static bool TryFormat(ushort value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((ulong)value, output, capacity, ref written);
    public static bool TryFormat(int value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((long)value, output, capacity, ref written);
    public static bool TryFormat(uint value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((ulong)value, output, capacity, ref written);
    public static bool TryFormat(nint value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((long)value, output, capacity, ref written);
    public static bool TryFormat(nuint value, char* output, nuint capacity, ref nuint written) nothrow
        => IntegerFormatting::TryFormat((ulong)value, output, capacity, ref written);
}
