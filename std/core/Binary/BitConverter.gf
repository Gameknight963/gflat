namespace std::Binary;

// Preserve IEEE floating-point representation, without numeric conversion.
public static class BitConverter
{
    public static uint FloatToUInt32Bits(float value) nothrow => value.reinterpret<uint>();
    public static float UInt32BitsToFloat(uint bits) nothrow => bits.reinterpret<float>();
    public static ulong DoubleToUInt64Bits(double value) nothrow => value.reinterpret<ulong>();
    public static double UInt64BitsToDouble(ulong bits) nothrow => bits.reinterpret<double>();
}
