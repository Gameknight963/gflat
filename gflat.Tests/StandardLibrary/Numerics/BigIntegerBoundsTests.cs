using System.Globalization;
using System.Text;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerBoundsTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_BOUNDS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasedOperandsAndAllocationFailuresPreserveIndependentOwners(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/BigInteger.gf"))
            .Append(new SourceFile("big-integer-bounds-ownership.gf", """
            using Standard;
            using Standard::Numerics;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            int failAt = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    int attempt = attempts;
                    attempts++;
                    if (attempt == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int main()
            {
                {
                    BigInteger value = BigInteger::Parse("-100000000000000000000000000000000000000000000000000");
                    BigInteger minimum = BigInteger::Parse("-999999999999999999999999999999999999999999999999999");
                    BigInteger maximum = minimum.Negate();
                    BigInteger saved = value;
                    BigInteger target = new(321l);
                    for (int method = 0; method < 4; method++)
                    {
                        attempts = 0; failAt = 0;
                        int beforeLive = live;
                        bool caught = false;
                        try
                        {
                            if (method == 0) target = BigInteger::Min(&value, &minimum);
                            else if (method == 1) target = BigInteger::Max(&value, &maximum);
                            else if (method == 2) target = BigInteger::Clamp(&value, &minimum, &maximum);
                            else target = BigInteger::CopySign(&value, &maximum);
                        }
                        catch (Exception* error) { caught = true; }
                        failAt = -1;
                        if (!caught || live != beforeLive || target.ToLong() != 321l || !value.Equals(&saved)) return 1;
                    }
                    BigInteger same = BigInteger::Min(&value, &value);
                    value = BigInteger::Clamp(&value, &value, &value);
                    if (!value.Equals(&saved) || !same.Equals(&saved)) return 2;
                    value = BigInteger::CopySign(&value, &value);
                    if (!value.Equals(&saved)) return 3;
                    BigInteger zero = new();
                    attempts = 0; failAt = 0;
                    BigInteger first = BigInteger::Min(&zero, &maximum);
                    BigInteger second = BigInteger::Max(&zero, &minimum);
                    BigInteger third = BigInteger::Clamp(&zero, &zero, &zero);
                    BigInteger fourth = BigInteger::CopySign(&zero, &minimum);
                    failAt = -1;
                    if (attempts != 0 || !first.IsZero || !second.IsZero || !third.IsZero || !fourth.IsZero) return 4;
                }
                if (live != 0) return 5;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Bounds ownership exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundsAndSignsMatchDotNetAndResultsOutliveInputs(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_bigint_bounds_" + Guid.NewGuid().ToString("N"));
        try
        {
            Integer huge = Integer.Pow(10, 100);
            Integer[] values = [-huge, long.MinValue, -1000000000, -1, 0, 1, 1000000000, long.MaxValue, huge];
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (Integer value in values)
            foreach (Integer minimum in values)
            foreach (Integer maximum in values)
            {
                bool valid = minimum <= maximum;
                Integer clamped = valid ? Integer.Clamp(value, minimum, maximum) : new Integer(123);
                Integer[] row = [value, minimum, maximum, Integer.Min(value, minimum), Integer.Max(value, minimum), clamped, Integer.CopySign(value, maximum)];
                byte[][] texts = row.Select(item => Encoding.ASCII.GetBytes(item.ToString(CultureInfo.InvariantCulture))).ToArray();
                foreach (byte[] text in texts) writer.Write(text.Length);
                writer.Write(valid ? 1 : 0);
                foreach (byte[] text in texts) writer.Write(text);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Numerics/BigInteger.gf"))
                .Append(new SourceFile("big-integer-bounds.gf", $$"""
                using Standard;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                BigInteger Read(void* file, int count)
                {
                    char[1024] text;
                    if (fread(&text[0], 1, (nuint)count, file) != (nuint)count) throw new Exception("fixture truncated");
                    return BigInteger::Parse(&text[0], (nuint)count);
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[8] header;
                    while (true)
                    {
                        nuint received = fread(&header[0], 1, 32, file!);
                        if (received == 0) break;
                        if (received != 32) return 2;
                        BigInteger value = Read(file!, header[0]);
                        BigInteger minimum = Read(file!, header[1]);
                        BigInteger maximum = Read(file!, header[2]);
                        BigInteger expectedMin = Read(file!, header[3]);
                        BigInteger expectedMax = Read(file!, header[4]);
                        BigInteger expectedClamp = Read(file!, header[5]);
                        BigInteger expectedSign = Read(file!, header[6]);
                        BigInteger actualMin = BigInteger::Min(&value, &minimum);
                        BigInteger actualMax = BigInteger::Max(&value, &minimum);
                        BigInteger actualClamp = new(123l);
                        bool caught = false;
                        try { actualClamp = BigInteger::Clamp(&value, &minimum, &maximum); }
                        catch (Exception* error) { caught = true; }
                        if (caught != (header[7] == 0)) return 3;
                        BigInteger actualSign = BigInteger::CopySign(&value, &maximum);
                        value = new BigInteger(); minimum = new BigInteger(); maximum = new BigInteger();
                        if (!actualMin.Equals(&expectedMin) || !actualMax.Equals(&expectedMax)) return 4;
                        if (!actualClamp.Equals(&expectedClamp) || !actualSign.Equals(&expectedSign)) return 5;
                    }
                    if (ferror(file!) != 0) return 6;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Integer bounds exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
