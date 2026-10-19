namespace std;

// Integer bit utilities with defined zero and masked-rotation behavior.
public static class BitOperations
{
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
