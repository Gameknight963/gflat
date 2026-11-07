namespace Standard::Binary;

// Preserve IEEE floating-point representation, without numeric conversion.
public static class BitConverter
{
    public static bool IsLittleEndian
    {
        get nothrow
        {
            ushort value = 1;
            return (&value).reinterpret<readonly(byte)*>()[0] == (byte)1;
        }
    }

    public static int FloatToInt32Bits(float value) nothrow => value.reinterpret<int>();
    public static float Int32BitsToFloat(int bits) nothrow => bits.reinterpret<float>();
    public static long DoubleToInt64Bits(double value) nothrow => value.reinterpret<long>();
    public static double Int64BitsToDouble(long bits) nothrow => bits.reinterpret<double>();

    public static uint FloatToUInt32Bits(float value) nothrow => value.reinterpret<uint>();
    public static float UInt32BitsToFloat(uint bits) nothrow => bits.reinterpret<float>();
    public static ulong DoubleToUInt64Bits(double value) nothrow => value.reinterpret<ulong>();
    public static double UInt64BitsToDouble(ulong bits) nothrow => bits.reinterpret<double>();
}
