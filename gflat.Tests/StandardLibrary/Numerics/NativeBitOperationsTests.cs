using System.Numerics;

namespace gflat.Tests.Std.Numerics;

public sealed class NativeBitOperationsTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("NATIVE_BIT_OPERATIONS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeAndSignedOperationsMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_native_bits_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (ulong raw in Values())
            foreach (int offset in new[] { int.MinValue, -129, -65, -32, -1, 0, 1, 31, 32, 63, 64, 129, int.MaxValue })
            {
                nuint value = (nuint)raw;
                ulong[] row = [raw, unchecked((ulong)offset), (ulong)BitOperations.PopCount(value),
                    (ulong)BitOperations.LeadingZeroCount(value), (ulong)BitOperations.TrailingZeroCount(value),
                    BitOperations.RotateLeft(value, offset), BitOperations.RotateRight(value, offset),
                    BitOperations.IsPow2(value) ? 1ul : 0ul, BitOperations.IsPow2((int)raw) ? 1ul : 0ul,
                    BitOperations.IsPow2((long)raw) ? 1ul : 0ul, BitOperations.IsPow2((nint)value) ? 1ul : 0ul,
                    BitOperations.RoundUpToPowerOf2(value)];
                foreach (ulong item in row) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            string program = $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Check(ulong* row) nothrow
                {
                    nuint value = (nuint)row[0];
                    int offset = (int)row[1];
                    if (BitOperations::PopCount(value) != (int)row[2]) return false;
                    if (BitOperations::LeadingZeroCount(value) != (int)row[3]) return false;
                    if (BitOperations::TrailingZeroCount(value) != (int)row[4]) return false;
                    if (BitOperations::RotateLeft(value, offset) != (nuint)row[5]) return false;
                    if (BitOperations::RotateRight(value, offset) != (nuint)row[6]) return false;
                    if (BitOperations::IsPowerOfTwo(value) != (row[7] == 1ul)) return false;
                    if (BitOperations::IsPowerOfTwo((int)row[0]) != (row[8] == 1ul)) return false;
                    if (BitOperations::IsPowerOfTwo((long)row[0]) != (row[9] == 1ul)) return false;
                    if (BitOperations::IsPowerOfTwo((nint)value) != (row[10] == 1ul)) return false;
                    if (BitOperations::RoundUpToPowerOfTwo(value) != (nuint)row[11]) return false;
                    return true;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[12] row = default(ulong[12]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 96, file!);
                        if (received == 0) break;
                        if (received != 96) return 2;
                        if (!Check(&row[0])) return 3;
                    }
                    if (ferror(file!) != 0) return 4;
                    return 0;
                }
                """;
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/BitOperations.gf")
                .Append(new SourceFile("native-bits.gf", program)), optimization);
            Assert.True(result.ExitCode == 0, $"Native bits exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<ulong> Values()
    {
        yield return 0; yield return ulong.MaxValue;
        for (int bit = 0; bit < 64; bit++)
        {
            ulong value = 1ul << bit;
            yield return value; yield return value - 1;
            if (value != ulong.MaxValue) yield return value + 1;
            yield return ~value;
        }
        var random = new Random(927184);
        byte[] bytes = new byte[8];
        for (int i = 0; i < 512; i++) { random.NextBytes(bytes); yield return BitConverter.ToUInt64(bytes); }
    }
}
