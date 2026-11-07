namespace gflat.Tests.Std.Numerics;

public sealed class RandomBytesTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("RANDOM_BYTES.md", 0);

    private static ulong Draw(ref ulong state)
    {
        unchecked
        {
            state += 0x9e3779b97f4a7c15;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9;
            value = (value ^ (value >> 27)) * 0x94d049bb133111eb;
            return value ^ (value >> 31);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PointerAndSpanFormsMatchSplitMixBytesAndConsumeOnlyCompleteWords(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_random_bytes_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (ulong seed in new[] { 0UL, 1UL, 42UL, 0x8000000000000000, ulong.MaxValue })
            for (int length = 0; length <= 130; length++)
            {
                ulong state = seed;
                byte[] expected = new byte[length];
                for (int start = 0; start < length; start += 8)
                {
                    ulong value = Draw(ref state);
                    for (int offset = 0; offset < 8 && start + offset < length; offset++)
                        expected[start + offset] = (byte)(value >> (offset * 8));
                }
                writer.Write(seed); writer.Write(length); writer.Write(expected); writer.Write(Draw(ref state));
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Numerics/PseudoRandom.gf", "core/Numerics/pseudo_random_bytes.gf"))
                .Append(new SourceFile("random-bytes.gf", $$"""
                using Standard;
                using Standard::Memory;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                extern "C" void free(void* storage);
                int allocations = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes) { allocations++; return null; }
                    public replace void Free(void* storage) nothrow { free(storage); }
                }
                void FillSpan(PseudoRandom* random, Span<byte> destination) nothrow => random.NextBytes(destination);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    byte[130] expected;
                    byte[132] output;
                    while (true)
                    {
                        ulong seed = 0ul;
                        nuint received = fread(&seed, 1, 8, file!);
                        if (received == 0) break;
                        if (received != 8) return 2;
                        int length = 0;
                        if (fread(&length, 1, 4, file!) != 4) return 3;
                        if (fread(&expected[0], 1, (nuint)length, file!) != (nuint)length) return 4;
                        ulong next = 0ul;
                        if (fread(&next, 1, 8, file!) != 8) return 5;
                        for (int form = 0; form < 4; form++)
                        {
                            for (nuint i = 0; i < 132; i++) output[i] = (byte)165;
                            PseudoRandom random = new(seed);
                            Span<byte> destination = new(&output[1], (nuint)length);
                            if (length == 0) destination = new Span<byte>();
                            if (form == 0) random.NextBytes(&output[1], (nuint)length);
                            else if (form == 1) FillSpan(&random, destination);
                            else if (form == 2) NextBytes(ref random, &output[1], (nuint)length);
                            else NextBytes(ref random, destination);
                            for (nuint i = 0; i < (nuint)length; i++)
                                if (output[i + 1] != expected[i]) return 6;
                            if (output[0] != (byte)165 || output[(nuint)length + 1] != (byte)165) return 7;
                            if (random.NextULong() != next) return 8;
                        }
                    }
                    if (ferror(file!) != 0 || allocations != 0) return 9;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Random bytes exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
