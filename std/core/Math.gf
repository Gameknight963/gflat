namespace std;

// Scalar arithmetic helpers. No libc or host-native math ABI dependency.
public static class Math
{
    public static sbyte Min(sbyte a, sbyte b) { if (a < b) return a; return b; }
    public static sbyte Max(sbyte a, sbyte b) { if (a > b) return a; return b; }
    public static sbyte Clamp(sbyte value, sbyte minimum, sbyte maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static byte Min(byte a, byte b) { if (a < b) return a; return b; }
    public static byte Max(byte a, byte b) { if (a > b) return a; return b; }
    public static byte Clamp(byte value, byte minimum, byte maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static short Min(short a, short b) { if (a < b) return a; return b; }
    public static short Max(short a, short b) { if (a > b) return a; return b; }
    public static short Clamp(short value, short minimum, short maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static ushort Min(ushort a, ushort b) { if (a < b) return a; return b; }
    public static ushort Max(ushort a, ushort b) { if (a > b) return a; return b; }
    public static ushort Clamp(ushort value, ushort minimum, ushort maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static int Min(int a, int b) { if (a < b) return a; return b; }
    public static int Max(int a, int b) { if (a > b) return a; return b; }
    public static int Clamp(int value, int minimum, int maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static uint Min(uint a, uint b) { if (a < b) return a; return b; }
    public static uint Max(uint a, uint b) { if (a > b) return a; return b; }
    public static uint Clamp(uint value, uint minimum, uint maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static long Min(long a, long b) { if (a < b) return a; return b; }
    public static long Max(long a, long b) { if (a > b) return a; return b; }
    public static long Clamp(long value, long minimum, long maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static ulong Min(ulong a, ulong b) { if (a < b) return a; return b; }
    public static ulong Max(ulong a, ulong b) { if (a > b) return a; return b; }
    public static ulong Clamp(ulong value, ulong minimum, ulong maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static nint Min(nint a, nint b) { if (a < b) return a; return b; }
    public static nint Max(nint a, nint b) { if (a > b) return a; return b; }
    public static nint Clamp(nint value, nint minimum, nint maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static nuint Min(nuint a, nuint b) { if (a < b) return a; return b; }
    public static nuint Max(nuint a, nuint b) { if (a > b) return a; return b; }
    public static nuint Clamp(nuint value, nuint minimum, nuint maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static sbyte Abs(sbyte value)
    {
        sbyte minimum = (sbyte)-128;
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < 0) return (sbyte)(-value);
        return value;
    }

    public static short Abs(short value)
    {
        short minimum = (short)-32768;
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < 0) return (short)(-value);
        return value;
    }

    public static int Abs(int value)
    {
        int minimum = -2147483647 - 1;
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < 0) return (int)(-value);
        return value;
    }

    public static long Abs(long value)
    {
        long minimum = -9223372036854775807l - 1l;
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < 0) return (long)(-value);
        return value;
    }

    public static nint Abs(nint value)
    {
        nint minimum = (nint)((nuint)1 << ((int)sizeof(nint) * 8 - 1));
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < 0) return (nint)(-value);
        return value;
    }

    public static bool IsNaN(float value) => value != value;
    public static bool IsFinite(float value)
    {
        uint bits = *((uint*)&value);
        return (bits & (uint)2139095040) != (uint)2139095040;
    }
    public static float Abs(float value)
    {
        uint bits = *((uint*)&value) & (uint)2147483647;
        return *((float*)&bits);
    }

    public static float Min(float a, float b)
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a < b) return a;
        if (a == b)
        {
            uint bits = *((uint*)&a) | *((uint*)&b);
            return *((float*)&bits);
        }
        return b;
    }

    public static float Max(float a, float b)
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a > b) return a;
        if (a == b)
        {
            uint bits = *((uint*)&a) & *((uint*)&b);
            return *((float*)&bits);
        }
        return b;
    }

    public static bool IsNaN(double value) => value != value;
    public static bool IsFinite(double value)
    {
        ulong bits = *((ulong*)&value);
        return (bits & 9218868437227405312ul) != 9218868437227405312ul;
    }
    public static double Abs(double value)
    {
        ulong bits = *((ulong*)&value) & 9223372036854775807ul;
        return *((double*)&bits);
    }

    public static double Min(double a, double b)
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a < b) return a;
        if (a == b)
        {
            ulong bits = *((ulong*)&a) | *((ulong*)&b);
            return *((double*)&bits);
        }
        return b;
    }

    public static double Max(double a, double b)
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a > b) return a;
        if (a == b)
        {
            ulong bits = *((ulong*)&a) & *((ulong*)&b);
            return *((double*)&bits);
        }
        return b;
    }

}
