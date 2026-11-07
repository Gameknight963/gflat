namespace Standard::Numerics;

public enum MidpointRounding
{
    ToEven,
    AwayFromZero,
    ToZero,
    ToNegativeInfinity,
    ToPositiveInfinity
}

// Scalar arithmetic helpers, without direct libc calls. Checked integer
// operations require the ordinary exception/allocation support.
public static class Math
{
    public static const double E = 2.71828182845904523536028747135266249775724709369995d;
    public static const double PI = 3.14159265358979323846264338327950288419716939937510d;
    public static const double Tau = 6.28318530717958647692528676655900576839433879875021d;

    public static float DegreesToRadians(float degrees) nothrow
        => (degrees * (float)PI) / 180.0f;

    public static double DegreesToRadians(double degrees) nothrow
        => (degrees * PI) / 180.0d;

    public static float RadiansToDegrees(float radians) nothrow
        => (radians * 180.0f) / (float)PI;

    public static double RadiansToDegrees(double radians) nothrow
        => (radians * 180.0d) / PI;

    public static int Sign(sbyte value) nothrow
    {
        if (value < (sbyte)0) return -1;
        if (value > (sbyte)0) return 1;
        return 0;
    }

    public static int Sign(byte value) nothrow
    {
        if (value > (byte)0) return 1;
        return 0;
    }

    public static int Sign(short value) nothrow
    {
        if (value < (short)0) return -1;
        if (value > (short)0) return 1;
        return 0;
    }

    public static int Sign(ushort value) nothrow
    {
        if (value > (ushort)0) return 1;
        return 0;
    }

    public static int Sign(int value) nothrow
    {
        if (value < (int)0) return -1;
        if (value > (int)0) return 1;
        return 0;
    }

    public static int Sign(uint value) nothrow
    {
        if (value > (uint)0) return 1;
        return 0;
    }

    public static int Sign(long value) nothrow
    {
        if (value < (long)0) return -1;
        if (value > (long)0) return 1;
        return 0;
    }

    public static int Sign(ulong value) nothrow
    {
        if (value > (ulong)0) return 1;
        return 0;
    }

    public static int Sign(extralong value) nothrow
    {
        if (value < (extralong)0) return -1;
        if (value > (extralong)0) return 1;
        return 0;
    }

    public static int Sign(nint value) nothrow
    {
        if (value < (nint)0) return -1;
        if (value > (nint)0) return 1;
        return 0;
    }

    public static int Sign(nuint value) nothrow
    {
        if (value > (nuint)0) return 1;
        return 0;
    }

    public static int Sign(float value)
    {
        if (value != value) throw new Exception("NaN has no sign");
        if (value < (float)0) return -1;
        if (value > (float)0) return 1;
        return 0;
    }

    public static int Sign(double value)
    {
        if (value != value) throw new Exception("NaN has no sign");
        if (value < (double)0) return -1;
        if (value > (double)0) return 1;
        return 0;
    }

    public static sbyte Min(sbyte a, sbyte b) nothrow { if (a < b) return a; return b; }
    public static sbyte Max(sbyte a, sbyte b) nothrow { if (a > b) return a; return b; }
    public static sbyte Clamp(sbyte value, sbyte minimum, sbyte maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static byte Min(byte a, byte b) nothrow { if (a < b) return a; return b; }
    public static byte Max(byte a, byte b) nothrow { if (a > b) return a; return b; }
    public static byte Clamp(byte value, byte minimum, byte maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static short Min(short a, short b) nothrow { if (a < b) return a; return b; }
    public static short Max(short a, short b) nothrow { if (a > b) return a; return b; }
    public static short Clamp(short value, short minimum, short maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static ushort Min(ushort a, ushort b) nothrow { if (a < b) return a; return b; }
    public static ushort Max(ushort a, ushort b) nothrow { if (a > b) return a; return b; }
    public static ushort Clamp(ushort value, ushort minimum, ushort maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static int Min(int a, int b) nothrow { if (a < b) return a; return b; }
    public static int Max(int a, int b) nothrow { if (a > b) return a; return b; }
    public static int Clamp(int value, int minimum, int maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static uint Min(uint a, uint b) nothrow { if (a < b) return a; return b; }
    public static uint Max(uint a, uint b) nothrow { if (a > b) return a; return b; }
    public static uint Clamp(uint value, uint minimum, uint maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static long Min(long a, long b) nothrow { if (a < b) return a; return b; }
    public static long Max(long a, long b) nothrow { if (a > b) return a; return b; }
    public static long Clamp(long value, long minimum, long maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static ulong Min(ulong a, ulong b) nothrow { if (a < b) return a; return b; }
    public static ulong Max(ulong a, ulong b) nothrow { if (a > b) return a; return b; }
    public static ulong Clamp(ulong value, ulong minimum, ulong maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static extralong Min(extralong a, extralong b) nothrow { if (a < b) return a; return b; }
    public static extralong Max(extralong a, extralong b) nothrow { if (a > b) return a; return b; }
    public static extralong Clamp(extralong value, extralong minimum, extralong maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static nint Min(nint a, nint b) nothrow { if (a < b) return a; return b; }
    public static nint Max(nint a, nint b) nothrow { if (a > b) return a; return b; }
    public static nint Clamp(nint value, nint minimum, nint maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static nuint Min(nuint a, nuint b) nothrow { if (a < b) return a; return b; }
    public static nuint Max(nuint a, nuint b) nothrow { if (a > b) return a; return b; }
    public static nuint Clamp(nuint value, nuint minimum, nuint maximum)
    {
        if (minimum > maximum) throw new Exception("Minimum must not exceed maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    // Integer division truncates toward zero. The remainder has the dividend's sign.
    // DivRem leaves the remainder unchanged on failure. TryDivRem clears both outputs.
    public static sbyte DivRem(sbyte dividend, sbyte divisor, ref sbyte remainder)
    {
        if (divisor == (sbyte)0) throw new Exception("Division by zero");
        sbyte minimum = (sbyte)-128;
        if (dividend == minimum && divisor == (sbyte)-1) throw new Exception("Division overflow");
        sbyte quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(sbyte dividend, sbyte divisor, ref sbyte quotient, ref sbyte remainder) nothrow
    {
        sbyte minimum = (sbyte)-128;
        if (divisor == (sbyte)0 || (dividend == minimum && divisor == (sbyte)-1))
        {
            quotient = (sbyte)0;
            remainder = (sbyte)0;
            return false;
        }
        sbyte divided = dividend / divisor;
        sbyte remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static byte DivRem(byte dividend, byte divisor, ref byte remainder)
    {
        if (divisor == (byte)0) throw new Exception("Division by zero");
        byte quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(byte dividend, byte divisor, ref byte quotient, ref byte remainder) nothrow
    {
        if (divisor == (byte)0)
        {
            quotient = (byte)0;
            remainder = (byte)0;
            return false;
        }
        byte divided = dividend / divisor;
        byte remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static short DivRem(short dividend, short divisor, ref short remainder)
    {
        if (divisor == (short)0) throw new Exception("Division by zero");
        short minimum = (short)-32768;
        if (dividend == minimum && divisor == (short)-1) throw new Exception("Division overflow");
        short quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(short dividend, short divisor, ref short quotient, ref short remainder) nothrow
    {
        short minimum = (short)-32768;
        if (divisor == (short)0 || (dividend == minimum && divisor == (short)-1))
        {
            quotient = (short)0;
            remainder = (short)0;
            return false;
        }
        short divided = dividend / divisor;
        short remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static ushort DivRem(ushort dividend, ushort divisor, ref ushort remainder)
    {
        if (divisor == (ushort)0) throw new Exception("Division by zero");
        ushort quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(ushort dividend, ushort divisor, ref ushort quotient, ref ushort remainder) nothrow
    {
        if (divisor == (ushort)0)
        {
            quotient = (ushort)0;
            remainder = (ushort)0;
            return false;
        }
        ushort divided = dividend / divisor;
        ushort remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static int DivRem(int dividend, int divisor, ref int remainder)
    {
        if (divisor == (int)0) throw new Exception("Division by zero");
        int minimum = -2147483647 - 1;
        if (dividend == minimum && divisor == (int)-1) throw new Exception("Division overflow");
        int quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(int dividend, int divisor, ref int quotient, ref int remainder) nothrow
    {
        int minimum = -2147483647 - 1;
        if (divisor == (int)0 || (dividend == minimum && divisor == (int)-1))
        {
            quotient = (int)0;
            remainder = (int)0;
            return false;
        }
        int divided = dividend / divisor;
        int remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static uint DivRem(uint dividend, uint divisor, ref uint remainder)
    {
        if (divisor == (uint)0) throw new Exception("Division by zero");
        uint quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(uint dividend, uint divisor, ref uint quotient, ref uint remainder) nothrow
    {
        if (divisor == (uint)0)
        {
            quotient = (uint)0;
            remainder = (uint)0;
            return false;
        }
        uint divided = dividend / divisor;
        uint remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static long DivRem(long dividend, long divisor, ref long remainder)
    {
        if (divisor == (long)0) throw new Exception("Division by zero");
        long minimum = -9223372036854775807l - 1l;
        if (dividend == minimum && divisor == (long)-1) throw new Exception("Division overflow");
        long quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(long dividend, long divisor, ref long quotient, ref long remainder) nothrow
    {
        long minimum = -9223372036854775807l - 1l;
        if (divisor == (long)0 || (dividend == minimum && divisor == (long)-1))
        {
            quotient = (long)0;
            remainder = (long)0;
            return false;
        }
        long divided = dividend / divisor;
        long remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static ulong DivRem(ulong dividend, ulong divisor, ref ulong remainder)
    {
        if (divisor == (ulong)0) throw new Exception("Division by zero");
        ulong quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(ulong dividend, ulong divisor, ref ulong quotient, ref ulong remainder) nothrow
    {
        if (divisor == (ulong)0)
        {
            quotient = (ulong)0;
            remainder = (ulong)0;
            return false;
        }
        ulong divided = dividend / divisor;
        ulong remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static extralong DivRem(extralong dividend, extralong divisor, ref extralong remainder)
    {
        if (divisor == (extralong)0) throw new Exception("Division by zero");
        extralong minimum = (extralong)1 << 127;
        if (dividend == minimum && divisor == (extralong)-1) throw new Exception("Division overflow");
        extralong quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(extralong dividend, extralong divisor, ref extralong quotient, ref extralong remainder) nothrow
    {
        extralong minimum = (extralong)1 << 127;
        if (divisor == (extralong)0 || (dividend == minimum && divisor == (extralong)-1))
        {
            quotient = (extralong)0;
            remainder = (extralong)0;
            return false;
        }
        extralong divided = dividend / divisor;
        extralong remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static nint DivRem(nint dividend, nint divisor, ref nint remainder)
    {
        if (divisor == (nint)0) throw new Exception("Division by zero");
        nint minimum = (nint)((nuint)1 << ((int)sizeof(nint) * 8 - 1));
        if (dividend == minimum && divisor == (nint)-1) throw new Exception("Division overflow");
        nint quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(nint dividend, nint divisor, ref nint quotient, ref nint remainder) nothrow
    {
        nint minimum = (nint)((nuint)1 << ((int)sizeof(nint) * 8 - 1));
        if (divisor == (nint)0 || (dividend == minimum && divisor == (nint)-1))
        {
            quotient = (nint)0;
            remainder = (nint)0;
            return false;
        }
        nint divided = dividend / divisor;
        nint remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static nuint DivRem(nuint dividend, nuint divisor, ref nuint remainder)
    {
        if (divisor == (nuint)0) throw new Exception("Division by zero");
        nuint quotient = dividend / divisor;
        remainder = dividend % divisor;
        return quotient;
    }

    public static bool TryDivRem(nuint dividend, nuint divisor, ref nuint quotient, ref nuint remainder) nothrow
    {
        if (divisor == (nuint)0)
        {
            quotient = (nuint)0;
            remainder = (nuint)0;
            return false;
        }
        nuint divided = dividend / divisor;
        nuint remaining = dividend % divisor;
        quotient = divided;
        remainder = remaining;
        return true;
    }

    public static uint GreatestCommonDivisor(uint a, uint b) nothrow
    {
        while (b != 0u) { uint remainder = a % b; a = b; b = remainder; }
        return a;
    }

    public static ulong GreatestCommonDivisor(ulong a, ulong b) nothrow
    {
        while (b != 0ul) { ulong remainder = a % b; a = b; b = remainder; }
        return a;
    }

    public static nuint GreatestCommonDivisor(nuint a, nuint b) nothrow
    {
        while (b != (nuint)0) { nuint remainder = a % b; a = b; b = remainder; }
        return a;
    }

    // Divide before multiplying so a common factor does not cause false overflow.
    public static bool TryLeastCommonMultiple(uint a, uint b, ref uint result) nothrow
    {
        result = 0u;
        if (a == 0u || b == 0u) return true;
        uint reduced = a / GreatestCommonDivisor(a, b);
        uint maximum = 0u - 1u;
        if (reduced > maximum / b) return false;
        result = reduced * b;
        return true;
    }

    public static bool TryLeastCommonMultiple(ulong a, ulong b, ref ulong result) nothrow
    {
        result = 0ul;
        if (a == 0ul || b == 0ul) return true;
        ulong reduced = a / GreatestCommonDivisor(a, b);
        ulong maximum = 0ul - 1ul;
        if (reduced > maximum / b) return false;
        result = reduced * b;
        return true;
    }

    public static bool TryLeastCommonMultiple(nuint a, nuint b, ref nuint result) nothrow
    {
        result = (nuint)0;
        if (a == (nuint)0 || b == (nuint)0) return true;
        nuint reduced = a / GreatestCommonDivisor(a, b);
        nuint maximum = (nuint)0 - (nuint)1;
        if (reduced > maximum / b) return false;
        result = reduced * b;
        return true;
    }

    public static uint LeastCommonMultiple(uint a, uint b)
    {
        uint result = 0u;
        if (!TryLeastCommonMultiple(a, b, ref result)) throw new Exception("Least common multiple overflow");
        return result;
    }

    public static ulong LeastCommonMultiple(ulong a, ulong b)
    {
        ulong result = 0ul;
        if (!TryLeastCommonMultiple(a, b, ref result)) throw new Exception("Least common multiple overflow");
        return result;
    }

    public static nuint LeastCommonMultiple(nuint a, nuint b)
    {
        nuint result = (nuint)0;
        if (!TryLeastCommonMultiple(a, b, ref result)) throw new Exception("Least common multiple overflow");
        return result;
    }

    public static sbyte CopySign(sbyte value, sbyte sign)
    {
        if ((value < (sbyte)0) == (sign < (sbyte)0)) return value;
        sbyte minimum = (sbyte)-128;
        if (value == minimum) throw new Exception("Copy sign overflow");
        return (sbyte)(-value);
    }

    public static short CopySign(short value, short sign)
    {
        if ((value < (short)0) == (sign < (short)0)) return value;
        short minimum = (short)-32768;
        if (value == minimum) throw new Exception("Copy sign overflow");
        return (short)(-value);
    }

    public static int CopySign(int value, int sign)
    {
        if ((value < (int)0) == (sign < (int)0)) return value;
        int minimum = -2147483647 - 1;
        if (value == minimum) throw new Exception("Copy sign overflow");
        return (int)(-value);
    }

    public static long CopySign(long value, long sign)
    {
        if ((value < (long)0) == (sign < (long)0)) return value;
        long minimum = -9223372036854775807l - 1l;
        if (value == minimum) throw new Exception("Copy sign overflow");
        return (long)(-value);
    }

    public static extralong CopySign(extralong value, extralong sign)
    {
        if ((value < (extralong)0) == (sign < (extralong)0)) return value;
        extralong minimum = (extralong)1 << 127;
        if (value == minimum) throw new Exception("Copy sign overflow");
        return (extralong)(-value);
    }

    public static nint CopySign(nint value, nint sign)
    {
        if ((value < (nint)0) == (sign < (nint)0)) return value;
        nint minimum = (nint)((nuint)1 << ((int)sizeof(nint) * 8 - 1));
        if (value == minimum) throw new Exception("Copy sign overflow");
        return (nint)(-value);
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

    public static extralong Abs(extralong value)
    {
        extralong minimum = (extralong)1 << 127;
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < (extralong)0) return -value;
        return value;
    }

    public static nint Abs(nint value)
    {
        nint minimum = (nint)((nuint)1 << ((int)sizeof(nint) * 8 - 1));
        if (value == minimum) throw new Exception("Absolute value overflow");
        if (value < 0) return (nint)(-value);
        return value;
    }

    // LLVM's IEEE square root, without fast-math flags or C math declarations.
    public static float Sqrt(float value) nothrow
        => __llvm_module<float>("declare float @llvm.sqrt.f32(float)\ndefine float @root(float %value) alwaysinline nounwind {\n%result = call float @llvm.sqrt.f32(float %value)\nret float %result\n}", "root", value);

    public static int ILogB(float value) nothrow
    {
        uint magnitude = value.reinterpret<uint>() & 0x7fffffffu;
        int exponent = (int)(magnitude >> 23);
        if (exponent == 255) return 2147483647;
        if (exponent != 0) return exponent - 127;
        if (magnitude == 0u) return -2147483647 - 1;
        int result = -126;
        while ((magnitude & 0x00800000u) == 0u) { magnitude = magnitude << 1; result--; }
        return result;
    }

    public static bool IsNaN(float value) nothrow => value != value;
    public static bool IsFinite(float value) nothrow
    {
        uint bits = value.reinterpret<uint>();
        return (bits & (uint)2139095040) != (uint)2139095040;
    }
    public static bool IsInfinity(float value) nothrow
        => (value.reinterpret<uint>() & 0x7fffffffu) == 0x7f800000u;
    public static bool IsPositiveInfinity(float value) nothrow
        => value.reinterpret<uint>() == 0x7f800000u;
    public static bool IsNegativeInfinity(float value) nothrow
        => value.reinterpret<uint>() == 0xff800000u;
    public static bool IsNegative(float value) nothrow
        => (value.reinterpret<uint>() & 0x80000000u) != 0u;
    public static bool IsPositive(float value) nothrow
        => (value.reinterpret<uint>() & 0x80000000u) == 0u;
    public static bool IsNormal(float value) nothrow
    {
        uint exponent = value.reinterpret<uint>() & 0x7f800000u;
        return exponent != 0u && exponent != 0x7f800000u;
    }
    public static bool IsSubnormal(float value) nothrow
    {
        uint magnitude = value.reinterpret<uint>() & 0x7fffffffu;
        return magnitude != 0u && magnitude < 0x00800000u;
    }

    public static float Abs(float value) nothrow
    {
        uint bits = value.reinterpret<uint>() & (uint)2147483647;
        return bits.reinterpret<float>();
    }

    public static float CopySign(float magnitude, float sign) nothrow
    {
        uint bits = (magnitude.reinterpret<uint>() & 2147483647u) | (sign.reinterpret<uint>() & 2147483648u);
        return bits.reinterpret<float>();
    }

    public static float BitIncrement(float value) nothrow
    {
        uint bits = value.reinterpret<uint>();
        uint magnitude = bits & 0x7fffffffu;
        if (magnitude > 0x7f800000u || bits == 0x7f800000u) return value;
        if (magnitude == 0u) bits = 1u;
        else if ((bits & 0x80000000u) != 0u) bits--;
        else bits++;
        return bits.reinterpret<float>();
    }

    public static float BitDecrement(float value) nothrow
    {
        uint bits = value.reinterpret<uint>();
        uint magnitude = bits & 0x7fffffffu;
        if (magnitude > 0x7f800000u || bits == 0xff800000u) return value;
        if (magnitude == 0u) bits = 0x80000001u;
        else if ((bits & 0x80000000u) != 0u) bits++;
        else bits--;
        return bits.reinterpret<float>();
    }

    public static float Truncate(float value) nothrow
    {
        uint bits = value.reinterpret<uint>();
        int exponent = (int)((bits >> 23) & 255u) - 127;
        if (exponent >= 23) return value;
        if (exponent < 0) bits = bits & 2147483648u;
        else
        {
            uint fraction = (1u << (23 - exponent)) - 1u;
            bits = bits & (fraction ^ 4294967295u);
        }
        return bits.reinterpret<float>();
    }

    public static float Floor(float value) nothrow
    {
        float result = Math::Truncate(value);
        if (value < result) return result - 1f;
        return result;
    }

    public static float Ceiling(float value) nothrow
    {
        float result = Math::Truncate(value);
        if (value > result) return result + 1f;
        return result;
    }

    public static float Round(float value) nothrow
    {
        uint bits = value.reinterpret<uint>();
        int exponent = (int)((bits >> 23) & 255u) - 127;
        if (exponent >= 23) return value;
        uint sign = bits & 2147483648u;
        if (exponent < -1) return sign.reinterpret<float>();
        if (exponent == -1)
        {
            // Exactly +/-0.5 ties with the even integer zero.
            if ((bits & 8388607u) != 0u) sign = sign | 1065353216u;
            return sign.reinterpret<float>();
        }
        int shift = 23 - exponent;
        uint unit = 1u << shift;
        uint mask = unit - 1u;
        uint discarded = bits & mask;
        uint significand = (bits & 8388607u) | 8388608u;
        uint truncated = bits & (mask ^ 4294967295u);
        uint half = unit >> 1;
        if (discarded > half || (discarded == half && ((significand >> shift) & 1u) != 0u))
            truncated += unit;
        return truncated.reinterpret<float>();
    }

    public static float Min(float a, float b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a < b) return a;
        if (a == b)
        {
            uint bits = a.reinterpret<uint>() | b.reinterpret<uint>();
            return bits.reinterpret<float>();
        }
        return b;
    }

    public static float Max(float a, float b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a > b) return a;
        if (a == b)
        {
            uint bits = a.reinterpret<uint>() & b.reinterpret<uint>();
            return bits.reinterpret<float>();
        }
        return b;
    }

    public static double Sqrt(double value) nothrow
        => __llvm_module<double>("declare double @llvm.sqrt.f64(double)\ndefine double @root(double %value) alwaysinline nounwind {\n%result = call double @llvm.sqrt.f64(double %value)\nret double %result\n}", "root", value);

    public static int ILogB(double value) nothrow
    {
        ulong magnitude = value.reinterpret<ulong>() & 0x7ffffffffffffffful;
        int exponent = (int)(magnitude >> 52);
        if (exponent == 2047) return 2147483647;
        if (exponent != 0) return exponent - 1023;
        if (magnitude == 0ul) return -2147483647 - 1;
        int result = -1022;
        while ((magnitude & 0x0010000000000000ul) == 0ul) { magnitude = magnitude << 1; result--; }
        return result;
    }

    public static bool IsNaN(double value) nothrow => value != value;
    public static bool IsFinite(double value) nothrow
    {
        ulong bits = value.reinterpret<ulong>();
        return (bits & 9218868437227405312ul) != 9218868437227405312ul;
    }
    public static bool IsInfinity(double value) nothrow
        => (value.reinterpret<ulong>() & 0x7ffffffffffffffful) == 0x7ff0000000000000ul;
    public static bool IsPositiveInfinity(double value) nothrow
        => value.reinterpret<ulong>() == 0x7ff0000000000000ul;
    public static bool IsNegativeInfinity(double value) nothrow
        => value.reinterpret<ulong>() == 0xfff0000000000000ul;
    public static bool IsNegative(double value) nothrow
        => (value.reinterpret<ulong>() & 0x8000000000000000ul) != 0ul;
    public static bool IsPositive(double value) nothrow
        => (value.reinterpret<ulong>() & 0x8000000000000000ul) == 0ul;
    public static bool IsNormal(double value) nothrow
    {
        ulong exponent = value.reinterpret<ulong>() & 0x7ff0000000000000ul;
        return exponent != 0ul && exponent != 0x7ff0000000000000ul;
    }
    public static bool IsSubnormal(double value) nothrow
    {
        ulong magnitude = value.reinterpret<ulong>() & 0x7ffffffffffffffful;
        return magnitude != 0ul && magnitude < 0x0010000000000000ul;
    }

    public static double Abs(double value) nothrow
    {
        ulong bits = value.reinterpret<ulong>() & 9223372036854775807ul;
        return bits.reinterpret<double>();
    }

    public static double CopySign(double magnitude, double sign) nothrow
    {
        ulong bits = (magnitude.reinterpret<ulong>() & 9223372036854775807ul) | (sign.reinterpret<ulong>() & 9223372036854775808ul);
        return bits.reinterpret<double>();
    }

    public static double BitIncrement(double value) nothrow
    {
        ulong bits = value.reinterpret<ulong>();
        ulong magnitude = bits & 0x7ffffffffffffffful;
        if (magnitude > 0x7ff0000000000000ul || bits == 0x7ff0000000000000ul) return value;
        if (magnitude == 0ul) bits = 1ul;
        else if ((bits & 0x8000000000000000ul) != 0ul) bits--;
        else bits++;
        return bits.reinterpret<double>();
    }

    public static double BitDecrement(double value) nothrow
    {
        ulong bits = value.reinterpret<ulong>();
        ulong magnitude = bits & 0x7ffffffffffffffful;
        if (magnitude > 0x7ff0000000000000ul || bits == 0xfff0000000000000ul) return value;
        if (magnitude == 0ul) bits = 0x8000000000000001ul;
        else if ((bits & 0x8000000000000000ul) != 0ul) bits++;
        else bits--;
        return bits.reinterpret<double>();
    }

    public static double Truncate(double value) nothrow
    {
        ulong bits = value.reinterpret<ulong>();
        int exponent = (int)((bits >> 52) & 2047ul) - 1023;
        if (exponent >= 52) return value;
        if (exponent < 0) bits = bits & 9223372036854775808ul;
        else
        {
            ulong fraction = (1ul << (52 - exponent)) - 1ul;
            bits = bits & (fraction ^ 18446744073709551615ul);
        }
        return bits.reinterpret<double>();
    }

    public static double Floor(double value) nothrow
    {
        double result = Math::Truncate(value);
        if (value < result) return result - 1.0;
        return result;
    }

    public static double Ceiling(double value) nothrow
    {
        double result = Math::Truncate(value);
        if (value > result) return result + 1.0;
        return result;
    }

    public static double Round(double value) nothrow
    {
        ulong bits = value.reinterpret<ulong>();
        int exponent = (int)((bits >> 52) & 2047ul) - 1023;
        if (exponent >= 52) return value;
        ulong sign = bits & 9223372036854775808ul;
        if (exponent < -1) return sign.reinterpret<double>();
        if (exponent == -1)
        {
            if ((bits & 4503599627370495ul) != 0ul) sign = sign | 4607182418800017408ul;
            return sign.reinterpret<double>();
        }
        int shift = 52 - exponent;
        ulong unit = 1ul << shift;
        ulong mask = unit - 1ul;
        ulong discarded = bits & mask;
        ulong significand = (bits & 4503599627370495ul) | 4503599627370496ul;
        ulong truncated = bits & (mask ^ 18446744073709551615ul);
        ulong half = unit >> 1;
        if (discarded > half || (discarded == half && ((significand >> shift) & 1ul) != 0ul))
            truncated += unit;
        return truncated.reinterpret<double>();
    }

    public static double Min(double a, double b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a < b) return a;
        if (a == b)
        {
            ulong bits = a.reinterpret<ulong>() | b.reinterpret<ulong>();
            return bits.reinterpret<double>();
        }
        return b;
    }

    public static double Max(double a, double b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        if (a > b) return a;
        if (a == b)
        {
            ulong bits = a.reinterpret<ulong>() & b.reinterpret<ulong>();
            return bits.reinterpret<double>();
        }
        return b;
    }


    public static float Clamp(float value, float minimum, float maximum)
    {
        if (minimum > maximum) throw new Exception("Clamp minimum exceeds maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static float MinMagnitude(float a, float b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        float left = Abs(a);
        float right = Abs(b);
        if (left < right) return a;
        if (left > right) return b;
        return Min(a, b);
    }

    public static float MaxMagnitude(float a, float b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        float left = Abs(a);
        float right = Abs(b);
        if (left > right) return a;
        if (left < right) return b;
        return Max(a, b);
    }

    public static double Clamp(double value, double minimum, double maximum)
    {
        if (minimum > maximum) throw new Exception("Clamp minimum exceeds maximum");
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    public static double MinMagnitude(double a, double b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        double left = Abs(a);
        double right = Abs(b);
        if (left < right) return a;
        if (left > right) return b;
        return Min(a, b);
    }

    public static double MaxMagnitude(double a, double b) nothrow
    {
        if (a != a) return a;
        if (b != b) return b;
        double left = Abs(a);
        double right = Abs(b);
        if (left > right) return a;
        if (left < right) return b;
        return Max(a, b);
    }

    public static float MinNumber(float a, float b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return Min(a, b);
    }

    public static float MaxNumber(float a, float b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return Max(a, b);
    }

    public static float MinMagnitudeNumber(float a, float b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return MinMagnitude(a, b);
    }

    public static float MaxMagnitudeNumber(float a, float b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return MaxMagnitude(a, b);
    }

    public static double MinNumber(double a, double b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return Min(a, b);
    }

    public static double MaxNumber(double a, double b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return Max(a, b);
    }

    public static double MinMagnitudeNumber(double a, double b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return MinMagnitude(a, b);
    }

    public static double MaxMagnitudeNumber(double a, double b) nothrow
    {
        if (b != b) return a;
        if (a != a) return b;
        return MaxMagnitude(a, b);
    }

    public static float Round(float value, MidpointRounding mode)
    {
        if (mode == MidpointRounding::ToEven) return Round(value);
        if (mode == MidpointRounding::ToZero) return Truncate(value);
        if (mode == MidpointRounding::ToNegativeInfinity) return Floor(value);
        if (mode == MidpointRounding::ToPositiveInfinity) return Ceiling(value);
        if (mode != MidpointRounding::AwayFromZero) throw new Exception("Invalid midpoint rounding mode");
        float whole = Truncate(value);
        if (Abs(value - whole) >= 0.5f) return whole + CopySign(1.0f, value);
        return whole;
    }

    public static double Round(double value, MidpointRounding mode)
    {
        if (mode == MidpointRounding::ToEven) return Round(value);
        if (mode == MidpointRounding::ToZero) return Truncate(value);
        if (mode == MidpointRounding::ToNegativeInfinity) return Floor(value);
        if (mode == MidpointRounding::ToPositiveInfinity) return Ceiling(value);
        if (mode != MidpointRounding::AwayFromZero) throw new Exception("Invalid midpoint rounding mode");
        double whole = Truncate(value);
        if (Abs(value - whole) >= 0.5d) return whole + CopySign(1.0d, value);
        return whole;
    }

    public static float Round(float value, int digits)
        => Round(value, digits, MidpointRounding::ToEven);

    public static double Round(double value, int digits)
        => Round(value, digits, MidpointRounding::ToEven);

    private static void ValidateRoundingMode(MidpointRounding mode)
    {
        if (mode != MidpointRounding::ToEven && mode != MidpointRounding::AwayFromZero &&
            mode != MidpointRounding::ToZero && mode != MidpointRounding::ToNegativeInfinity &&
            mode != MidpointRounding::ToPositiveInfinity)
            throw new Exception("Invalid midpoint rounding mode");
    }

    public static float Round(float value, int digits, MidpointRounding mode)
    {
        if (digits < 0 || digits > 6) throw new Exception("Float rounding digits must be between 0 and 6");
        ValidateRoundingMode(mode);
        if (Abs(value) >= 100000000.0f || IsNaN(value)) return value;
        float factor = 1.0f;
        for (int i = 0; i < digits; i++) factor *= 10.0f;
        return Round(value * factor, mode) / factor;
    }

    public static double Round(double value, int digits, MidpointRounding mode)
    {
        if (digits < 0 || digits > 15) throw new Exception("Double rounding digits must be between 0 and 15");
        ValidateRoundingMode(mode);
        if (Abs(value) >= 10000000000000000.0d || IsNaN(value)) return value;
        double factor = 1.0d;
        for (int i = 0; i < digits; i++) factor *= 10.0d;
        return Round(value * factor, mode) / factor;
    }

    public static float ScaleB(float value, int exponent) nothrow
    {
        uint bits = value.reinterpret<uint>();
        uint sign = bits & 0x80000000u;
        uint significand = bits & 0x7fffffu;
        long encoded = (long)((bits >> 23) & (uint)255);
        if (encoded == 255l || (encoded == 0l && significand == 0u)) return value;
        if (encoded == 0l)
        {
            encoded = 1l;
            while (significand < 0x800000u) { significand = significand << 1; encoded--; }
        }
        else significand = significand | 0x800000u;
        long scaled = encoded + (long)exponent;
        if (scaled >= 255l)
        {
            uint infinity = sign | 0x7f800000u;
            return infinity.reinterpret<float>();
        }
        if (scaled > 0l)
        {
            uint normal = sign | ((uint)scaled << 23) | (significand & 0x7fffffu);
            return normal.reinterpret<float>();
        }
        long distance = 1l - scaled;
        if (distance > 24l) return sign.reinterpret<float>();
        int shift = (int)distance;
        uint rounded = significand >> shift;
        uint tail = significand & ((1u << shift) - 1u);
        uint halfway = 1u << (shift - 1);
        if (tail > halfway || (tail == halfway && (rounded & 1u) != 0u)) rounded++;
        uint result = sign | rounded;
        return result.reinterpret<float>();
    }

    public static double ScaleB(double value, int exponent) nothrow
    {
        ulong bits = value.reinterpret<ulong>();
        ulong sign = bits & 0x8000000000000000ul;
        ulong significand = bits & 0xffffffffffffful;
        long encoded = (long)((bits >> 52) & (ulong)2047);
        if (encoded == 2047l || (encoded == 0l && significand == 0ul)) return value;
        if (encoded == 0l)
        {
            encoded = 1l;
            while (significand < 0x10000000000000ul) { significand = significand << 1; encoded--; }
        }
        else significand = significand | 0x10000000000000ul;
        long scaled = encoded + (long)exponent;
        if (scaled >= 2047l)
        {
            ulong infinity = sign | 0x7ff0000000000000ul;
            return infinity.reinterpret<double>();
        }
        if (scaled > 0l)
        {
            ulong normal = sign | ((ulong)scaled << 52) | (significand & 0xffffffffffffful);
            return normal.reinterpret<double>();
        }
        long distance = 1l - scaled;
        if (distance > 53l) return sign.reinterpret<double>();
        int shift = (int)distance;
        ulong rounded = significand >> shift;
        ulong tail = significand & ((1ul << shift) - 1ul);
        ulong halfway = 1ul << (shift - 1);
        if (tail > halfway || (tail == halfway && (rounded & 1ul) != 0ul)) rounded++;
        ulong result = sign | rounded;
        return result.reinterpret<double>();
    }
    public static long BigMul(int left, int right) nothrow => (long)left * (long)right;
    public static ulong BigMul(uint left, uint right) nothrow => (ulong)left * (ulong)right;
    public static extralong BigMul(long left, long right) nothrow => (extralong)left * (extralong)right;

    public static long BigMul(long left, long right, ref long low) nothrow
    {
        extralong product = (extralong)left * (extralong)right;
        low = (long)product;
        return (long)(product >> 64);
    }

    public static ulong BigMul(ulong left, ulong right, ref ulong low) nothrow
    {
        ulong first = (ulong)(uint)left * (ulong)(uint)right;
        ulong second = (ulong)(uint)left * (right >> 32);
        ulong third = (left >> 32) * (ulong)(uint)right;
        ulong fourth = (left >> 32) * (right >> 32);
        ulong middle = (first >> 32) + (ulong)(uint)second + (ulong)(uint)third;
        low = (middle << 32) | (ulong)(uint)first;
        return fourth + (second >> 32) + (third >> 32) + (middle >> 32);
    }

}
