namespace std;

// Invariant decimal/hex text into caller storage, without allocation or a terminator.
// The written-count storage must be disjoint from output. Failure writes zero
// to written and leaves the character buffer untouched.
public static class IntegerFormatting
{
    // High-to-low base-2^32 long division. Each partial dividend fits in ulong.
    private static uint DivideDecimal(ref uint high, ref uint upper, ref uint lower, ref uint low) nothrow
    {
        ulong part = (ulong)high;
        high = (uint)(part / 10ul);
        ulong remainder = part % 10ul;
        part = (remainder << 32) | (ulong)upper;
        upper = (uint)(part / 10ul);
        remainder = part % 10ul;
        part = (remainder << 32) | (ulong)lower;
        lower = (uint)(part / 10ul);
        remainder = part % 10ul;
        part = (remainder << 32) | (ulong)low;
        low = (uint)(part / 10ul);
        return (uint)(part % 10ul);
    }

    public static bool TryFormat(extralong value, char* output, nuint capacity, ref nuint written) nothrow
    {
        uint high = (uint)(value >> 96);
        uint upper = (uint)(value >> 64);
        uint lower = (uint)(value >> 32);
        uint low = (uint)value;
        bool negative = value < (extralong)0;
        if (negative)
        {
            // Complement and carry across limbs, including the signed minimum.
            ulong part = (ulong)(low ^ 4294967295u) + 1ul;
            low = (uint)part;
            part = (ulong)(lower ^ 4294967295u) + (part >> 32);
            lower = (uint)part;
            part = (ulong)(upper ^ 4294967295u) + (part >> 32);
            upper = (uint)part;
            high = (high ^ 4294967295u) + (uint)(part >> 32);
        }
        uint h = high; uint u = upper; uint l = lower; uint b = low;
        nuint length = 0;
        while (true)
        {
            DivideDecimal(ref h, ref u, ref l, ref b); length++;
            if ((h | u | l | b) == 0u) break;
        }
        if (negative) length++;
        written = 0;
        if (capacity < length) return false;
        nuint cursor = length;
        while (true)
        {
            uint digit = DivideDecimal(ref high, ref upper, ref lower, ref low);
            cursor--;
            output[cursor] = (char)(48u + digit);
            if ((high | upper | lower | low) == 0u) break;
        }
        if (negative) output[0] = '-';
        written = length;
        return true;
    }

    public static bool TryFormatHex(extralong value, char* output, nuint capacity,
        ref nuint written, nuint minimumDigits = 1, bool uppercase = true) nothrow
    {
        nuint length = 32;
        if (value >= (extralong)0)
        {
            length = 1;
            extralong remaining = value;
            while (remaining >= (extralong)16) { remaining = remaining >> 4; length++; }
        }
        nuint naturalDigits = length;
        if (minimumDigits > length) length = minimumDigits;
        written = 0;
        if (capacity < length) return false;
        readonly(char)* digits = "0123456789ABCDEF";
        if (!uppercase) digits = "0123456789abcdef";
        nuint cursor = length;
        for (nuint i = 0; i < naturalDigits; i++)
        {
            cursor--;
            output[cursor] = digits[(int)(value & (extralong)15)];
            value = value >> 4;
        }
        while (cursor != 0) { cursor--; output[cursor] = '0'; }
        written = length;
        return true;
    }

    private static bool HexMagnitude(ulong value, char* output, nuint capacity,
        ref nuint written, bool uppercase, nuint minimumDigits) nothrow
    {
        written = 0;
        nuint length = 1;
        ulong remaining = value;
        while (remaining >= 16ul) { remaining = remaining >> 4; length++; }
        if (minimumDigits > length) length = minimumDigits;
        if (capacity < length) return false;
        readonly(char)* digits = "0123456789ABCDEF";
        if (!uppercase) digits = "0123456789abcdef";
        nuint cursor = length;
        while (true)
        {
            cursor--;
            output[cursor] = digits[(int)(value & 15ul)];
            value = value >> 4;
            if (value == 0ul) break;
        }
        while (cursor != 0) { cursor--; output[cursor] = '0'; }
        written = length;
        return true;
    }

    public static bool TryFormatHex(ulong value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude(value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(long value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(uint value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(int value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)(uint)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(byte value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(sbyte value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)(byte)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(ushort value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(short value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)(ushort)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(nuint value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)value, output, capacity, ref written, uppercase, minimumDigits);

    public static bool TryFormatHex(nint value, char* output, nuint capacity, ref nuint written,
        nuint minimumDigits = 1, bool uppercase = true) nothrow
        => HexMagnitude((ulong)(nuint)value, output, capacity, ref written, uppercase, minimumDigits);

    private static bool Magnitude(ulong value, bool negative, char* output,
        nuint capacity, ref nuint written) nothrow
    {
        nuint length = 1;
        ulong remaining = value;
        while (remaining >= 10ul) { remaining /= 10ul; length++; }
        if (negative) length++;
        written = 0;
        if (capacity < length) return false;
        // Write backwards only after preflight. A temporary-buffer copy can be
        // lowered to an external memcpy call by LLVM, introducing a CRT dependency.
        nuint cursor = length;
        while (true)
        {
            cursor--;
            output[cursor] = (char)(48ul + value % 10ul);
            value /= 10ul;
            if (value == 0ul) break;
        }
        if (negative) output[0] = '-';
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
