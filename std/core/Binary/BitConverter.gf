namespace std;

// Preserve IEEE floating-point representation, without numeric conversion.
public static class BitConverter
{
    public static uint FloatToUInt32Bits(float value) nothrow => *((uint*)&value);
    public static float UInt32BitsToFloat(uint bits) nothrow => *((float*)&bits);
    public static ulong DoubleToUInt64Bits(double value) nothrow => *((ulong*)&value);
    public static double UInt64BitsToDouble(ulong bits) nothrow => *((double*)&bits);
}
