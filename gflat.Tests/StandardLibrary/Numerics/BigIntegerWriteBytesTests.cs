using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed partial class BigIntegerWriteBytesTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_WRITE_BYTES.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Numerics/BigInteger.gf"))
        .Append(new SourceFile("big-integer-write-bytes.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ByteCountsAndCallerBufferExportsMatchDotNetAtSignBoundariesAndRandomWidths(int optimization)
    {
        var values = Enumerable.Range(-256, 513).Select(value => new Integer(value)).ToList();
        for (int bit = 0; bit <= 256; bit += 8)
        {
            Integer power = Integer.One << bit;
            foreach (Integer value in new[] { power - 1, power, power + 1 })
            {
                values.Add(value);
                values.Add(-value);
            }
        }
        values.Add(ulong.MaxValue);
        values.Add(-(Integer)ulong.MaxValue);
        var random = new Random(719853);
        for (int i = 0; i < 64; i++)
        {
            byte[] bytes = new byte[random.Next(1, 129)];
            random.NextBytes(bytes);
            values.Add(new Integer(bytes));
        }
        values = values.Distinct().ToList();
        string path = Path.Combine(Path.GetTempPath(), "gflat_bigint_write_bytes_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (Integer value in values)
            {
                byte[] input = value.ToByteArray();
                writer.Write(input.Length);
                writer.Write(input);
                for (int form = 0; form < 4; form++)
                {
                    bool unsigned = form >= 2, bigEndian = form % 2 != 0;
                    if (unsigned && value.Sign < 0) { writer.Write(-1); continue; }
                    byte[] encoded = value.ToByteArray(unsigned, bigEndian);
                    Assert.Equal(value.GetByteCount(unsigned), encoded.Length);
                    writer.Write(encoded.Length);
                    writer.Write(encoded);
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::Memory;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    byte[129] input;
                    byte[129] expected;
                    byte[132] output;
                    for (int row = 0; row < {{values.Count}}; row++)
                    {
                        int count = 0;
                        if (fread(&count, 1, 4, file!) != 4) return 2;
                        if (fread(&input[0], 1, (nuint)count, file!) != (nuint)count) return 3;
                        BigInteger value = BigInteger::FromBytes(&input[0], (nuint)count);
                        for (int form = 0; form < 4; form++)
                        {
                            if (fread(&count, 1, 4, file!) != 4) return 4;
                            bool unsigned = form >= 2;
                            bool bigEndian = form % 2 != 0;
                            if (count < 0)
                            {
                                bool caught = false;
                                try { value.GetByteCount(unsigned); }
                                catch (Exception* error) { caught = true; }
                                if (!caught) return 5;
                                for (nuint capacity = 0; capacity <= 129; capacity += 129)
                                {
                                    for (nuint i = 0; i < 132; i++) output[i] = (byte)165;
                                    nuint written = 77;
                                    caught = false;
                                    try { value.TryWriteBytes(new Span<byte>(&output[1], capacity), ref written, unsigned, bigEndian); }
                                    catch (Exception* error) { caught = true; }
                                    if (!caught || written != 0) return 6;
                                    for (nuint i = 0; i < 132; i++) if (output[i] != (byte)165) return 7;
                                }
                                continue;
                            }
                            if (fread(&expected[0], 1, (nuint)count, file!) != (nuint)count) return 8;
                            if (value.GetByteCount(unsigned) != (nuint)count) return 9;
                            for (nuint capacity = 0; capacity <= (nuint)count + 1; capacity++)
                            {
                                if (count > 9 && capacity != 0 && capacity != (nuint)count - 1
                                    && capacity != (nuint)count && capacity != (nuint)count + 1) continue;
                                for (nuint i = 0; i < 132; i++) output[i] = (byte)165;
                                nuint written = 77;
                                bool fits = capacity >= (nuint)count;
                                bool result = value.TryWriteBytes(new Span<byte>(&output[1], capacity), ref written, unsigned, bigEndian);
                                if (result != fits) return 10;
                                if (fits && written != (nuint)count || !fits && written != 0) return 11;
                                for (nuint i = 0; i < 132; i++)
                                {
                                    byte wanted = (byte)165;
                                    if (fits && i >= 1 && i <= (nuint)count) wanted = expected[i - 1];
                                    if (output[i] != wanted) return 12;
                                }
                                if (capacity == (nuint)count)
                                {
                                    BigInteger decoded = BigInteger::FromBytes(&output[1], written, unsigned, bigEndian);
                                    if (!decoded.Equals(&value)) return 13;
                                }
                            }
                        }
                    }
                    if (ferror(file!) != 0) return 14;
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }
}
