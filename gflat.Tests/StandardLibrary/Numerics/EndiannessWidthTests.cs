using System.Buffers.Binary;

namespace gflat.Tests.Std.Numerics;

public sealed class EndiannessWidthTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("INTEGER_ENDIANNESS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeAnd128BitReversalMatchDotNetWithoutRuntimeSupport(int optimization)
    {
        var values = new HashSet<UInt128> { 0, UInt128.MaxValue, (UInt128)0x0102030405060708ul << 64 | 0x090a0b0c0d0e0f10ul };
        for (int bit = 0; bit < 128; bit++)
        {
            values.Add(UInt128.One << bit);
            values.Add(~(UInt128.One << bit));
        }
        var random = new Random(920417);
        for (int i = 0; i < 128; i++)
            values.Add((UInt128)(ulong)random.NextInt64(long.MinValue, long.MaxValue) << 64 |
                (ulong)random.NextInt64(long.MinValue, long.MaxValue));
        string path = Path.Combine(Path.GetTempPath(), "gflat_endian_width_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (UInt128 value in values)
            {
                UInt128 reversed = (UInt128)BinaryPrimitives.ReverseEndianness((Int128)value);
                writer.Write((ulong)value); writer.Write((ulong)(value >> 64));
                writer.Write((ulong)reversed); writer.Write((ulong)(reversed >> 64));
                writer.Write((ulong)BinaryPrimitives.ReverseEndianness((nuint)(ulong)value));
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string sourcePath = Path.Combine(AppContext.BaseDirectory, "Standard", "core", "Numerics", "BitOperations.gf");
            string ir = Compiler.Emit([new SourceFile(sourcePath, File.ReadAllText(sourcePath)), new SourceFile("endian-width.gf", $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode) nothrow;
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file) nothrow;
                extern "C" int fclose(void* file) nothrow;
                int main() nothrow
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int i = 0; i < {{values.Count}}; i++)
                    {
                        ulong low = 0ul;
                        ulong high = 0ul;
                        ulong expectedLow = 0ul;
                        ulong expectedHigh = 0ul;
                        ulong expectedNative = 0ul;
                        if (fread(&low, 8, 1, file!) != 1 || fread(&high, 8, 1, file!) != 1 ||
                            fread(&expectedLow, 8, 1, file!) != 1 || fread(&expectedHigh, 8, 1, file!) != 1 ||
                            fread(&expectedNative, 8, 1, file!) != 1) return 2;
                        extralong value = ((extralong)high << 64) | (extralong)low;
                        extralong actual = BitOperations::ReverseEndianness(value);
                        if ((ulong)actual != expectedLow || (ulong)(actual >> 64) != expectedHigh) return 3;
                        if (BitOperations::ReverseEndianness(actual) != value) return 4;
                        if (BitOperations::ReverseEndianness((nuint)low) != (nuint)expectedNative) return 5;
                        if (BitOperations::ReverseEndianness((nint)low) != (nint)expectedNative) return 6;
                    }
                    for (int i = 0; i < 256; i++)
                    {
                        if (BitOperations::ReverseEndianness((byte)i) != (byte)i) return 7;
                        if (BitOperations::ReverseEndianness((sbyte)i) != (sbyte)i) return 8;
                    }
                    return 0;
                }
                """)], options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
            foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "@malloc", "@free", "__gflat_gc" })
                Assert.DoesNotContain(forbidden, ir);
            _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        }
        finally { File.Delete(path); }
    }
}
