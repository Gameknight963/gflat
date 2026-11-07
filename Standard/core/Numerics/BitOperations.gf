namespace Standard::Numerics;

// Integer bit utilities with defined zero and masked-rotation behavior.
public static class BitOperations
{
    public static int Log2(uint value) nothrow => 31 - LeadingZeroCount(value | 1u);
    public static int Log2(ulong value) nothrow => 63 - LeadingZeroCount(value | 1ul);
    public static int Log2(nuint value) nothrow => Log2((ulong)value);

    public static byte ReverseEndianness(byte value) nothrow => value;
    public static sbyte ReverseEndianness(sbyte value) nothrow => value;

    public static nuint ReverseEndianness(nuint value) nothrow
    {
        static if (sizeof(nuint) == 8) return (nuint)ReverseEndianness((ulong)value);
        else return (nuint)ReverseEndianness((uint)value);
    }

    public static nint ReverseEndianness(nint value) nothrow
        => (nint)ReverseEndianness((nuint)value);

    public static extralong ReverseEndianness(extralong value) nothrow
    {
        ulong lower = ReverseEndianness((ulong)value);
        ulong upper = ReverseEndianness((ulong)(value >> 64));
        return ((extralong)lower << 64) | (extralong)upper;
    }

    public static ushort ReverseEndianness(ushort value) nothrow
        => (ushort)(((uint)value >> 8) | ((uint)value << 8));

    public static short ReverseEndianness(short value) nothrow
        => (short)ReverseEndianness((ushort)value);

    public static uint ReverseEndianness(uint value) nothrow
    {
        uint bytes = ((value & 0x00ff00ffu) << 8) | ((value >> 8) & 0x00ff00ffu);
        return (bytes << 16) | (bytes >> 16);
    }

    public static int ReverseEndianness(int value) nothrow
        => (int)ReverseEndianness((uint)value);

    public static ulong ReverseEndianness(ulong value) nothrow
    {
        ulong bytes = ((value & 0x00ff00ff00ff00fful) << 8) | ((value >> 8) & 0x00ff00ff00ff00fful);
        ulong halves = ((bytes & 0x0000ffff0000fffful) << 16) | ((bytes >> 16) & 0x0000ffff0000fffful);
        return (halves << 32) | (halves >> 32);
    }

    public static long ReverseEndianness(long value) nothrow
        => (long)ReverseEndianness((ulong)value);

    public static int PopCount(uint value) nothrow
    {
        int count = 0;
        while (value != 0u) { value = value & (value - 1u); count++; }
        return count;
    }

    public static int LeadingZeroCount(uint value) nothrow
    {
        if (value == 0u) return 32;
        int count = 0;
        uint bit = 2147483648u;
        while ((value & bit) == 0u) { count++; bit = bit >> 1; }
        return count;
    }

    public static int TrailingZeroCount(uint value) nothrow
    {
        if (value == 0u) return 32;
        int count = 0;
        while ((value & 1u) == 0u) { count++; value = value >> 1; }
        return count;
    }

    public static uint RotateLeft(uint value, int offset) nothrow
    {
        int bits = offset & 31;
        if (bits == 0) return value;
        return (value << bits) | (value >> (32 - bits));
    }

    public static uint RotateRight(uint value, int offset) nothrow
    {
        int bits = offset & 31;
        if (bits == 0) return value;
        return (value >> bits) | (value << (32 - bits));
    }

    public static bool IsPowerOfTwo(uint value) nothrow
        => value != 0u && (value & (value - 1u)) == 0u;

    // Zero and an unrepresentable rounded result both return zero.
    public static uint RoundUpToPowerOfTwo(uint value) nothrow
    {
        if (value == 0u) return 0u;
        value = value - 1u;
        value = value | (value >> 1);
        value = value | (value >> 2);
        value = value | (value >> 4);
        value = value | (value >> 8);
        value = value | (value >> 16);
        return value + 1u;
    }

    public static int PopCount(ulong value) nothrow
    {
        int count = 0;
        while (value != 0ul) { value = value & (value - 1ul); count++; }
        return count;
    }

    public static int LeadingZeroCount(ulong value) nothrow
    {
        if (value == 0ul) return 64;
        int count = 0;
        ulong bit = 9223372036854775808ul;
        while ((value & bit) == 0ul) { count++; bit = bit >> 1; }
        return count;
    }

    public static int TrailingZeroCount(ulong value) nothrow
    {
        if (value == 0ul) return 64;
        int count = 0;
        while ((value & 1ul) == 0ul) { count++; value = value >> 1; }
        return count;
    }

    public static ulong RotateLeft(ulong value, int offset) nothrow
    {
        int bits = offset & 63;
        if (bits == 0) return value;
        return (value << bits) | (value >> (64 - bits));
    }

    public static ulong RotateRight(ulong value, int offset) nothrow
    {
        int bits = offset & 63;
        if (bits == 0) return value;
        return (value >> bits) | (value << (64 - bits));
    }

    public static bool IsPowerOfTwo(ulong value) nothrow
        => value != 0ul && (value & (value - 1ul)) == 0ul;

    public static ulong RoundUpToPowerOfTwo(ulong value) nothrow
    {
        if (value == 0ul) return 0ul;
        value = value - 1ul;
        value = value | (value >> 1);
        value = value | (value >> 2);
        value = value | (value >> 4);
        value = value | (value >> 8);
        value = value | (value >> 16);
        value = value | (value >> 32);
        return value + 1ul;
    }

    public static int PopCount(nuint value) nothrow => PopCount((ulong)value);

    public static int LeadingZeroCount(nuint value) nothrow
        => LeadingZeroCount((ulong)value) - (64 - (int)sizeof(nuint) * 8);

    public static int TrailingZeroCount(nuint value) nothrow
    {
        if (value == 0) return (int)sizeof(nuint) * 8;
        return TrailingZeroCount((ulong)value);
    }

    public static nuint RotateLeft(nuint value, int offset) nothrow
    {
        static if (sizeof(nuint) == 8) return (nuint)RotateLeft((ulong)value, offset);
        else return (nuint)RotateLeft((uint)value, offset);
    }

    public static nuint RotateRight(nuint value, int offset) nothrow
    {
        static if (sizeof(nuint) == 8) return (nuint)RotateRight((ulong)value, offset);
        else return (nuint)RotateRight((uint)value, offset);
    }

    public static bool IsPowerOfTwo(nuint value) nothrow => IsPowerOfTwo((ulong)value);

    public static nuint RoundUpToPowerOfTwo(nuint value) nothrow
        => (nuint)RoundUpToPowerOfTwo((ulong)value);

    public static int PopCount(int value) nothrow => PopCount((uint)value);
    public static int LeadingZeroCount(int value) nothrow => LeadingZeroCount((uint)value);
    public static int TrailingZeroCount(int value) nothrow => TrailingZeroCount((uint)value);
    public static int RotateLeft(int value, int offset) nothrow => (int)RotateLeft((uint)value, offset);
    public static int RotateRight(int value, int offset) nothrow => (int)RotateRight((uint)value, offset);

    public static int PopCount(long value) nothrow => PopCount((ulong)value);
    public static int LeadingZeroCount(long value) nothrow => LeadingZeroCount((ulong)value);
    public static int TrailingZeroCount(long value) nothrow => TrailingZeroCount((ulong)value);
    public static long RotateLeft(long value, int offset) nothrow => (long)RotateLeft((ulong)value, offset);
    public static long RotateRight(long value, int offset) nothrow => (long)RotateRight((ulong)value, offset);

    public static int PopCount(nint value) nothrow => PopCount((nuint)value);
    public static int LeadingZeroCount(nint value) nothrow => LeadingZeroCount((nuint)value);
    public static int TrailingZeroCount(nint value) nothrow => TrailingZeroCount((nuint)value);
    public static nint RotateLeft(nint value, int offset) nothrow => (nint)RotateLeft((nuint)value, offset);
    public static nint RotateRight(nint value, int offset) nothrow => (nint)RotateRight((nuint)value, offset);

    public static int PopCount(extralong value) nothrow
        => PopCount((ulong)value) + PopCount((ulong)(value >> 64));

    public static int LeadingZeroCount(extralong value) nothrow
    {
        ulong upper = (ulong)(value >> 64);
        if (upper != 0ul) return LeadingZeroCount(upper);
        return 64 + LeadingZeroCount((ulong)value);
    }

    public static int TrailingZeroCount(extralong value) nothrow
    {
        ulong lower = (ulong)value;
        if (lower != 0ul) return TrailingZeroCount(lower);
        return 64 + TrailingZeroCount((ulong)(value >> 64));
    }

    public static extralong RotateLeft(extralong value, int offset) nothrow
    {
        int shift = offset & 127;
        ulong lower = (ulong)value;
        ulong upper = (ulong)(value >> 64);
        if (shift >= 64)
        {
            ulong temporary = lower;
            lower = upper;
            upper = temporary;
            shift -= 64;
        }
        if (shift != 0)
        {
            ulong nextLower = (lower << shift) | (upper >> (64 - shift));
            upper = (upper << shift) | (lower >> (64 - shift));
            lower = nextLower;
        }
        return ((extralong)upper << 64) | (extralong)lower;
    }

    public static extralong RotateRight(extralong value, int offset) nothrow
        => RotateLeft(value, (128 - (offset & 127)) & 127);

    public static bool IsPowerOfTwo(extralong value) nothrow
        => value > (extralong)0 && PopCount(value) == 1;

    public static bool IsPowerOfTwo(int value) nothrow
        => value > 0 && IsPowerOfTwo((uint)value);

    public static bool IsPowerOfTwo(long value) nothrow
        => value > 0l && IsPowerOfTwo((ulong)value);

    public static bool IsPowerOfTwo(nint value) nothrow
        => value > 0 && IsPowerOfTwo((nuint)value);

}
