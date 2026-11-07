using System.Buffers.Binary;

namespace gflat.Tests.Std.Binary;

public sealed class ReverseEndiannessTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("REVERSE_ENDIANNESS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllSixteenBitValuesAndRandomWideValuesMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_reverse_endian_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (low, high) in Inputs())
            {
                UInt128 value = ((UInt128)high << 64) | low;
                UInt128 reversed = BinaryPrimitives.ReverseEndianness(value);
                ulong[] row = [low, high, BinaryPrimitives.ReverseEndianness((ushort)low),
                    BinaryPrimitives.ReverseEndianness((uint)low), BinaryPrimitives.ReverseEndianness(low),
                    (ulong)reversed, (ulong)(reversed >> 64)];
                foreach (ulong item in row) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Binary/BinaryPrimitives.gf")
                .Append(new SourceFile("reverse-endian.gf", $$"""
                using Standard::Binary;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Check(ulong low, ulong high, ulong r16, ulong r32, ulong r64, ulong rLow, ulong rHigh) nothrow
                {
                    if (BinaryPrimitives::ReverseEndianness((byte)low) != (byte)low) return false;
                    if (BinaryPrimitives::ReverseEndianness((sbyte)low) != (sbyte)low) return false;
                    if (BinaryPrimitives::ReverseEndianness((ushort)low) != (ushort)r16) return false;
                    if (BinaryPrimitives::ReverseEndianness((short)low) != (short)r16) return false;
                    if (BinaryPrimitives::ReverseEndianness((uint)low) != (uint)r32) return false;
                    if (BinaryPrimitives::ReverseEndianness((int)low) != (int)r32) return false;
                    if (BinaryPrimitives::ReverseEndianness(low) != r64) return false;
                    if (BinaryPrimitives::ReverseEndianness((long)low) != (long)r64) return false;
                    nuint nativeExpected = (nuint)r32;
                    static if (sizeof(nuint) == 8) nativeExpected = (nuint)r64;
                    if (BinaryPrimitives::ReverseEndianness((nuint)low) != nativeExpected) return false;
                    if (BinaryPrimitives::ReverseEndianness((nint)low) != (nint)nativeExpected) return false;
                    extralong value = ((extralong)high << 64) | (extralong)low;
                    extralong reversed = BinaryPrimitives::ReverseEndianness(value);
                    if ((ulong)reversed != rLow || (ulong)(reversed >> 64) != rHigh) return false;
                    if (BinaryPrimitives::ReverseEndianness(reversed) != value) return false;
                    return true;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[7] row = default(ulong[7]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 56, file!);
                        if (received == 0) break;
                        if (received != 56) return 2;
                        if (!Check(row[0], row[1], row[2], row[3], row[4], row[5], row[6])) return 3;
                    }
                    if (ferror(file!) != 0) return 4;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Endian reversal exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<(ulong, ulong)> Inputs()
    {
        for (ulong value = 0; value <= ushort.MaxValue; value++) yield return (value, ~value);
        for (int bit = 0; bit < 64; bit++)
        {
            yield return (1ul << bit, 0);
            yield return (0, 1ul << bit);
            yield return (~(1ul << bit), ulong.MaxValue);
        }
        ulong[] boundaries = [0, 1, ulong.MaxValue, 0x8000000000000000, 0x7fffffffffffffff, 0x0102030405060708, 0xff00ff00ff00ff00];
        foreach (ulong low in boundaries)
        foreach (ulong high in boundaries) yield return (low, high);
        var random = new Random(274819);
        byte[] bytes = new byte[16];
        for (int i = 0; i < 8192; i++)
        {
            random.NextBytes(bytes);
            yield return (BitConverter.ToUInt64(bytes), BitConverter.ToUInt64(bytes, 8));
        }
    }
}
