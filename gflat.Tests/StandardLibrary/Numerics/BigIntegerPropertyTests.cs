using System.Globalization;
using System.Text;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerPropertyTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_PROPERTIES.md", 0);

    private static IEnumerable<Integer> Cases()
    {
        for (int i = -256; i <= 256; i++) yield return new Integer(i);
        Integer power = Integer.One;
        for (int i = 0; i <= 256; i++)
        {
            foreach (Integer value in new[] { power - 1, power, power + 1 })
            {
                yield return value;
                yield return -value;
            }
            power *= 10;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PropertiesMatchDotNetAcrossDecimalLimbsAndNeedNoAllocations(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_bigint_properties_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (Integer value in Cases())
            {
                byte[] text = Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
                int flags = (value.IsZero ? 1 : 0) | (value.IsOne ? 2 : 0) | (value.IsEven ? 4 : 0);
                writer.Write(text.Length); writer.Write(flags); writer.Write(text);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Numerics/BigInteger.gf"))
                .Append(new SourceFile("big-integer-properties.gf", $$"""
                using Standard;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int attempts = 0;
                int live = 0;
                bool deny = false;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        attempts++;
                        if (deny) return null;
                        void*? result = malloc(bytes);
                        if (result != null) live++;
                        return result;
                    }
                    public replace void Free(void* storage) nothrow { live--; free(storage); }
                }
                bool Check(immutable(BigInteger)* value, int flags) nothrow
                    => value.IsZero == ((flags & 1) != 0) && value.IsOne == ((flags & 2) != 0) && value.IsEven == ((flags & 4) != 0);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[2] header;
                    char[1024] text;
                    while (true)
                    {
                        nuint received = fread(&header[0], 1, 8, file!);
                        if (received == 0) break;
                        if (received != 8) return 2;
                        if (fread(&text[0], 1, (nuint)header[0], file!) != (nuint)header[0]) return 3;
                        {
                            BigInteger value = BigInteger::Parse(&text[0], (nuint)header[0]);
                            BigInteger saved = value;
                            readonly(BigInteger)* shallow = &value;
                            int before = attempts;
                            deny = true;
                            if (!Check(&value, header[1])) return 4;
                            if (shallow.IsZero != value.IsZero || shallow.IsOne != value.IsOne || shallow.IsEven != value.IsEven) return 5;
                            deny = false;
                            if (attempts != before || !value.Equals(&saved)) return 6;
                        }
                        if (live != 0) return 7;
                    }
                    if (ferror(file!) != 0) return 8;
                    BigInteger canonical = BigInteger::Parse("-0000");
                    if (!canonical.IsZero || !canonical.IsEven || canonical.IsOne) return 9;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Integer properties exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
