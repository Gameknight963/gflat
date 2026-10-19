namespace std;

// Integer bit utilities with defined zero and masked-rotation behavior.
public static class BitOperations
{
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

}
