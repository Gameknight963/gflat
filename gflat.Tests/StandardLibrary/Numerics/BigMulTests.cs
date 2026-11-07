using System.Numerics;

namespace gflat.Tests.Std.Numerics;

public sealed class BigMulTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MATH_BIG_MUL.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullProductsMatchDotNetAndBigIntegerIncludingAliasedLowOutputs(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_big_mul_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (left, right) in Inputs())
            {
                BigInteger unsigned = (BigInteger)left * right;
                BigInteger signed = (BigInteger)(long)left * (long)right;
                ulong low = (ulong)(unsigned & ulong.MaxValue);
                ulong high = (ulong)(unsigned >> 64);
                ulong signedLow = (ulong)(signed & ulong.MaxValue);
                ulong signedHigh = unchecked((ulong)(long)(signed >> 64));
                Assert.Equal(high, Math.BigMul(left, right, out ulong dotnetLow));
                Assert.Equal(low, dotnetLow);
                Assert.Equal((long)signedHigh, Math.BigMul((long)left, (long)right, out long dotnetSignedLow));
                Assert.Equal((long)signedLow, dotnetSignedLow);
                ulong[] row = [left, right, low, high, signedLow, signedHigh,
                    unchecked((ulong)((long)(int)left * (int)right)), (ulong)(uint)left * (uint)right];
                foreach (ulong item in row) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("big-mul.gf", $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Check(ulong* row) nothrow
                {
                    if (Math::BigMul((int)row[0], (int)row[1]) != (long)row[6]) return false;
                    if (Math::BigMul((uint)row[0], (uint)row[1]) != row[7]) return false;
                    extralong full = Math::BigMul((long)row[0], (long)row[1]);
                    if ((ulong)full != row[4] || (ulong)(full >> 64) != row[5]) return false;
                    ulong low = 0ul;
                    if (Math::BigMul(row[0], row[1], ref low) != row[3] || low != row[2]) return false;
                    long signedLow = 0l;
                    if (Math::BigMul((long)row[0], (long)row[1], ref signedLow) != (long)row[5] || signedLow != (long)row[4]) return false;
                    ulong left = row[0];
                    if (Math::BigMul(left, row[1], ref left) != row[3] || left != row[2]) return false;
                    long right = (long)row[1];
                    if (Math::BigMul((long)row[0], right, ref right) != (long)row[5] || right != (long)row[4]) return false;
                    return true;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[8] row = default(ulong[8]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 64, file!);
                        if (received == 0) break;
                        if (received != 64) return 2;
                        if (!Check(&row[0])) return 3;
                    }
                    if (ferror(file!) != 0) return 4;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"BigMul exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<(ulong, ulong)> Inputs()
    {
        ulong[] values = [0, 1, 2, uint.MaxValue, 1ul << 32, (1ul << 32) + 1, 0x7fffffffffffffff,
            0x8000000000000000, 0xffffffff00000000, ulong.MaxValue - 1, ulong.MaxValue];
        foreach (ulong left in values)
        foreach (ulong right in values) yield return (left, right);
        for (int bit = 0; bit < 64; bit++)
        {
            yield return (1ul << bit, ulong.MaxValue);
            yield return (~(1ul << bit), ~(1ul << (63 - bit)));
        }
        var random = new Random(712984);
        byte[] bytes = new byte[16];
        for (int i = 0; i < 8192; i++)
        {
            random.NextBytes(bytes);
            yield return (BitConverter.ToUInt64(bytes), BitConverter.ToUInt64(bytes, 8));
        }
    }
}
