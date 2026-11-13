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

    public static int FloatToInt32Bits(float value) nothrow
        => __llvm_module<int>("define i32 @convert(float %value) alwaysinline nounwind {\n%bits = bitcast float %value to i32\nret i32 %bits\n}", "convert", value);
    public static float Int32BitsToFloat(int bits) nothrow
        => __llvm_module<float>("define float @convert(i32 %value) alwaysinline nounwind {\n%bits = bitcast i32 %value to float\nret float %bits\n}", "convert", bits);
    public static long DoubleToInt64Bits(double value) nothrow
        => __llvm_module<long>("define i64 @convert(double %value) alwaysinline nounwind {\n%bits = bitcast double %value to i64\nret i64 %bits\n}", "convert", value);
    public static double Int64BitsToDouble(long bits) nothrow
        => __llvm_module<double>("define double @convert(i64 %value) alwaysinline nounwind {\n%bits = bitcast i64 %value to double\nret double %bits\n}", "convert", bits);

    public static uint FloatToUInt32Bits(float value) nothrow
        => __llvm_module<uint>("define i32 @convert(float %value) alwaysinline nounwind {\n%bits = bitcast float %value to i32\nret i32 %bits\n}", "convert", value);
    public static float UInt32BitsToFloat(uint bits) nothrow
        => __llvm_module<float>("define float @convert(i32 %value) alwaysinline nounwind {\n%bits = bitcast i32 %value to float\nret float %bits\n}", "convert", bits);
    public static ulong DoubleToUInt64Bits(double value) nothrow
        => __llvm_module<ulong>("define i64 @convert(double %value) alwaysinline nounwind {\n%bits = bitcast double %value to i64\nret i64 %bits\n}", "convert", value);
    public static double UInt64BitsToDouble(ulong bits) nothrow
        => __llvm_module<double>("define double @convert(i64 %value) alwaysinline nounwind {\n%bits = bitcast i64 %value to double\nret double %bits\n}", "convert", bits);
}
