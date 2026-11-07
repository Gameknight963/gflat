using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class RationalRoundingTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_ROUNDING.md", 0);

    private static BigInteger Round(BigInteger n, BigInteger d, MidpointRounding mode)
    {
        BigInteger q = BigInteger.DivRem(n, d, out BigInteger r);
        BigInteger floor = q - (n.Sign < 0 && !r.IsZero ? 1 : 0);
        BigInteger ceiling = q + (n.Sign > 0 && !r.IsZero ? 1 : 0);
        if (mode == MidpointRounding.ToZero) return q;
        if (mode == MidpointRounding.ToNegativeInfinity) return floor;
        if (mode == MidpointRounding.ToPositiveInfinity) return ceiling;
        int comparison = ((n - floor * d) * 2).CompareTo(d);
        if (comparison < 0) return floor;
        if (comparison > 0) return ceiling;
        return mode == MidpointRounding.ToEven ? (floor.IsEven ? floor : ceiling) : (n.Sign < 0 ? floor : ceiling);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactRoundingMatchesDecimalAndBigIntegerOraclesAndPreservesOwnersOnFailure(int optimization)
    {
        var cases = new List<(BigInteger N, BigInteger D)>();
        for (int n = -24; n <= 24; n++) for (int d = 1; d <= 9; d++) cases.Add((n, d));
        for (int digits = 20; digits <= 120; digits += 20)
        {
            BigInteger whole = BigInteger.Pow(10, digits);
            cases.Add((whole * 2 + 1, 2)); cases.Add((-whole * 2 - 3, 2));
            cases.Add((whole * 7 + 3, 7)); cases.Add((-whole * 7 - 4, 7));
        }
        string path = Path.Combine(Path.GetTempPath(), "gflat_rational_rounding_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(cases.Count);
                void Text(string value) { byte[] bytes = Encoding.ASCII.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
                foreach (var (n, d) in cases)
                {
                    Text(n.ToString(CultureInfo.InvariantCulture) + "/" + d.ToString(CultureInfo.InvariantCulture));
                    foreach (MidpointRounding mode in Enum.GetValues<MidpointRounding>())
                    {
                        BigInteger expected = Round(n, d, mode);
                        if (BigInteger.Abs(n) <= 24 && d <= 9)
                            Assert.Equal(expected, new BigInteger(decimal.Round((decimal)n / (decimal)d, 0, mode)));
                        Text(expected.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var sources = StandardTestSources.Combine(StandardTestSources.Numerics,
                StandardTestSources.Load("core/Numerics/rational_round.gf"))
                .Append(new SourceFile("rational-round.gf", $$"""
                    using Standard;
                    using Standard::Numerics;
                    using Standard::Text;

                    extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                    extern "C" nuint fread(void* data, nuint size, nuint count, void* file);
                    extern "C" int fclose(void* file);
                    extern "C" void*? malloc(nuint bytes);
                    extern "C" void free(void* storage);
                    int attempts = 0;
                    int live = 0;
                    int failAfter = -1;
                    namespace Memory
                    {
                        public replace void*? Allocate(nuint bytes)
                        {
                            attempts++;
                            if (failAfter == 0) { failAfter = -1; return null; }
                            if (failAfter > 0) failAfter--;
                            void*? result = malloc(bytes);
                            if (result != null) live++;
                            return result;
                        }
                        public replace void Free(void* storage) nothrow { live--; free(storage); }
                    }
                    nuint ReadText(void* file, char* output)
                    {
                        int count = 0;
                        if (fread(&count, 4, 1, file) != 1 || count < 0 || count > 2048) throw new Exception("bad fixture");
                        if (fread(output, 1, (nuint)count, file) != (nuint)count) throw new Exception("short fixture");
                        return (nuint)count;
                    }
                    int main()
                    {
                        void*? file = fopen({{literal}}, "rb");
                        if (file == null) return 1;
                        defer { fclose(file!); }
                        int cases = 0;
                        if (fread(&cases, 4, 1, file!) != 1) return 2;
                        char[2048] input;
                        char[2048] expected;
                        for (int row = 0; row < cases; row++)
                        {
                            {
                                nuint length = ReadText(file!, &input[0]);
                                Rational source = Rational::Parse(&input[0], length);
                                String before = source.ToString();
                                for (int mode = 0; mode < 5; mode++)
                                {
                                    length = ReadText(file!, &expected[0]);
                                    BigInteger rounded = source.Round((MidpointRounding)mode);
                                    String actual = rounded.ToString();
                                    if (!actual.Equals(&expected[0], length)) return 3;
                                    if (mode == 0)
                                    {
                                        BigInteger defaultResult = Round(&source);
                                        if (!defaultResult.Equals(&rounded)) return 4;
                                    }
                                }
                                String after = source.ToString();
                                if (!before.Equals(&after)) return 5;
                            }
                            if (live != 0) return 6;
                        }
                        {
                            Rational source = Rational::Parse("-10000000000000000000000000000000000000003/2");
                            String before = source.ToString();
                            for (int mode = 0; mode < 5; mode++)
                            {
                                int allocations = 0;
                                {
                                    int first = attempts;
                                    BigInteger result = source.Round((MidpointRounding)mode);
                                    allocations = attempts - first;
                                }
                                for (int failure = 0; failure < allocations; failure++)
                                {
                                    BigInteger output = new(99l);
                                    int owners = live;
                                    failAfter = failure;
                                    bool caught = false;
                                    try { output = source.Round((MidpointRounding)mode); }
                                    catch (Exception* error) { caught = true; }
                                    failAfter = -1;
                                    if (!caught || output.ToLong() != 99l || live != owners) return 7;
                                    String after = source.ToString();
                                    if (!before.Equals(&after)) return 8;
                                }
                            }
                            for (int invalid = -1; invalid <= 5; invalid += 6)
                            {
                                BigInteger output = new(99l);
                                bool caught = false;
                                try { output = source.Round((MidpointRounding)invalid); }
                                catch (Exception* error) { caught = true; }
                                if (!caught || output.ToLong() != 99l) return 9;
                            }
                        }
                        return live;
                    }
                    """));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
