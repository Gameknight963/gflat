namespace gflat.Tests.Std.Numerics;

public sealed class RandomTextTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/PseudoRandom.gf", "core/Numerics/pseudo_random_text.gf"))
            .Append(new SourceFile("random-text.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Random text exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private static ulong Next(ref ulong state)
    {
        state = unchecked(state + 0x9e3779b97f4a7c15ul);
        ulong value = state;
        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ul);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebul);
        return value ^ (value >> 31);
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("RANDOM_TEXT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedBytesMatchIndependentSamplingIncludingEmbeddedZeros(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_random_text_" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[][] alphabets = [[0], [0, 65, 255], [65, 66, 67, 68, 69, 70, 71], Enumerable.Range(0, 256).Select(x => (byte)x).ToArray()];
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (byte[] alphabet in alphabets)
            foreach (int length in new[] { 0, 1, 17, 257 })
            {
                ulong seed = 0x918273fffful + (ulong)length;
                ulong state = seed;
                var expected = new byte[length];
                ulong bound = (ulong)alphabet.Length;
                ulong threshold = unchecked(0ul - bound) % bound;
                for (int i = 0; i < length; i++)
                {
                    ulong word;
                    do { word = Next(ref state); } while (word < threshold);
                    expected[i] = alphabet[(int)(word % bound)];
                }
                writer.Write(seed); writer.Write(bound); writer.Write((ulong)length); writer.Write(Next(ref state));
                writer.Write(alphabet); writer.Write(expected);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::Memory;
                using Standard::Text;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int row = 0; row < 16; row++)
                    {
                        ulong[4] header = default(ulong[4]);
                        if (fread(&header[0], 8, 4, file!) != 4) return 2;
                        char[256] choices = default(char[256]);
                        char[257] expected = default(char[257]);
                        nuint count = (nuint)header[1];
                        nuint length = (nuint)header[2];
                        if (fread(&choices[0], 1, count, file!) != count || fread(&expected[0], 1, length, file!) != length) return 3;
                        PseudoRandom random = new(header[0]);
                        String output = random.GetString(new Span<readonly(char)>(&choices[0], count), length);
                        if (output.Length != length || output.Data[length] != '\0') return 4;
                        for (nuint i = 0; i < length; i++) { if (output[i] != expected[i]) return 5; }
                        if (random.NextULong() != header[3]) return 6;
                    }
                    PseudoRandom a = new(12ul);
                    PseudoRandom b = new(12ul);
                    String cstring = a.GetString("abcdefg", 50);
                    String counted = GetString(ref b, new Span<readonly(char)>("abcdefg", 7), 50);
                    if (!cstring.Equals(&counted) || a.NextULong() != b.NextULong()) return 7;
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HexFormsAndCasingMatchIndependentDraws(int optimization)
    {
        ulong state = 42;
        byte[] expected = new byte[129];
        for (int i = 0; i < expected.Length; i++) expected[i] = (byte)"0123456789ABCDEF"[(int)(Next(ref state) % 16)];
        ulong after = Next(ref state);
        Run($$"""
            using Standard;
            using Standard::Memory;
            using Standard::Text;
            using Standard::Numerics;

            int main()
            {
                PseudoRandom upper = new(42ul);
                PseudoRandom lower = new(42ul);
                PseudoRandom filled = new(42ul);
                String a = upper.GetHexString(129);
                String b = GetHexString(ref lower, (nuint)129, true);
                char[131] output = default(char[131]);
                output[0] = '!'; output[130] = '!';
                filled.GetHexString(new Span<char>(&output[1], 129));
                readonly(char)* expected = "{{System.Text.Encoding.ASCII.GetString(expected)}}";
                for (nuint i = 0; i < 129; i++)
                {
                    if (a[i] != expected[i] || output[i + 1] != expected[i]) return 1;
                    char small = expected[i];
                    if (small >= 'A' && small <= 'F') small = (char)(small + 32);
                    if (b[i] != small) return 2;
                }
                if (output[0] != '!' || output[130] != '!' || a.Data[129] != '\0' || b.Data[129] != '\0') return 3;
                if (upper.NextULong() != {{after}}ul || lower.NextULong() != {{after}}ul || filled.NextULong() != {{after}}ul) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndFailedAllocationsPreserveGeneratorAndExistingOutput(int optimization)
        => Run("""
            using Standard;
            using Standard::Memory;
            using Standard::Text;
            using Standard::Numerics;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; if (deny) return null; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int main()
            {
                PseudoRandom random = new(99ul);
                PseudoRandom reference = new(99ul);
                String output = s"unchanged";
                for (int form = 0; form < 4; form++)
                {
                    bool caught = false;
                    deny = true;
                    try
                    {
                        if (form == 0) output = random.GetString("", 0);
                        if (form == 1) { Span<readonly(char)> empty = new(); output = random.GetString(empty, 20); }
                        if (form == 2) output = random.GetString("abc", 20);
                        if (form == 3) output = random.GetHexString(20);
                    }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (!caught || !output.Equals("unchanged") || random.NextULong() != reference.NextULong()) return 1;
                }
                int before = attempts;
                deny = true;
                String empty = random.GetString("x", 0);
                String hex = random.GetHexString(0);
                Span<char> buffer = new();
                random.GetHexString(buffer);
                deny = false;
                if (!empty.IsEmpty || !hex.IsEmpty || attempts != before || random.NextULong() != reference.NextULong()) return 2;
                return 0;
            }
            """, optimization);
}
