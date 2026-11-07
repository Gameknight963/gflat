using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class CStringSubstringTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("CSTRING_SEARCH.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Load(
        "core/Text/ByteSearch.gf", "core/Text/CString.gf")
        .Append(new SourceFile("cstring-substring.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PatternSearchMatchesOrdinalPrefixSearchIncludingOverlapsAndPeriodicInputs(int optimization)
    {
        byte[][] texts = [[], "aaaaabaaaaab"u8.ToArray(), "ababababab"u8.ToArray(),
            "prefix/suffix"u8.ToArray(), [255, 128, 255], [0, 65, 66], [65, 0, 65, 66]];
        byte[][] patterns = [[], "a"u8.ToArray(), "aa"u8.ToArray(), "aaaab"u8.ToArray(),
            "abab"u8.ToArray(), "suffix"u8.ToArray(), [255], [128, 255], [65, 0, 66]];
        var cases = (from text in texts from pattern in patterns select (Text: text, Pattern: pattern)).ToList();
        var random = new Random(419753);
        for (int trial = 0; trial < 512; trial++)
        {
            byte[] text = new byte[random.Next(129)];
            random.NextBytes(text);
            byte[] pattern;
            if (text.Length != 0 && trial % 3 == 0)
            {
                int start = random.Next(text.Length);
                pattern = text[start..random.Next(start, text.Length + 1)];
            }
            else { pattern = new byte[random.Next(25)]; random.NextBytes(pattern); }
            if (trial % 7 == 0 && text.Length != 0) text[random.Next(text.Length)] = 0;
            cases.Add((text, pattern));
        }
        cases.Add((Encoding.ASCII.GetBytes(new string('a', 6000) + "b"), Encoding.ASCII.GetBytes(new string('a', 1024) + "b")));
        cases.Add((Encoding.ASCII.GetBytes(new string('a', 6000) + "b"), Encoding.ASCII.GetBytes(new string('a', 1024) + "c")));
        static string Prefix(byte[] bytes)
        {
            int zero = Array.IndexOf(bytes, (byte)0);
            return Encoding.Latin1.GetString(zero < 0 ? bytes : bytes[..zero]);
        }
        string path = Path.Combine(Path.GetTempPath(), "gflat_cstring_pattern_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var row in cases)
            {
                string text = Prefix(row.Text), pattern = Prefix(row.Pattern);
                writer.Write(row.Text.Length); writer.Write(row.Pattern.Length);
                writer.Write(text.IndexOf(pattern, StringComparison.Ordinal));
                writer.Write(text.LastIndexOf(pattern, StringComparison.Ordinal));
                writer.Write(row.Text); writer.Write(row.Pattern);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard::Text;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode) nothrow;
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file) nothrow;
                extern "C" int fclose(void* file) nothrow;
                int main() nothrow
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    int[4] header;
                    char[8194] text;
                    char[2050] pattern;
                    for (int row = 0; row < {{cases.Count}}; row++)
                    {
                        if (fread(&header[0], 1, 16, file!) != 16) return 2;
                        nuint textLength = (nuint)header[0];
                        nuint patternLength = (nuint)header[1];
                        if (fread(&text[1], 1, textLength, file!) != textLength
                            || fread(&pattern[1], 1, patternLength, file!) != patternLength) return 3;
                        text[0] = '!'; text[textLength + 1] = '\0'; text[textLength + 2] = '?';
                        pattern[0] = '!'; pattern[patternLength + 1] = '\0'; pattern[patternLength + 2] = '?';
                        nuint first = CString::NotFound;
                        nuint last = CString::NotFound;
                        if (header[2] >= 0) first = (nuint)header[2];
                        if (header[3] >= 0) last = (nuint)header[3];
                        if (CString::IndexOf(&text[1], &pattern[1]) != first
                            || CString::LastIndexOf(&text[1], &pattern[1]) != last
                            || CString::Contains(&text[1], &pattern[1]) != (header[2] >= 0)) return 4;
                        if (text[0] != '!' || text[textLength + 1] != '\0' || text[textLength + 2] != '?'
                            || pattern[0] != '!' || pattern[patternLength + 1] != '\0' || pattern[patternLength + 2] != '?') return 5;
                    }
                    fclose(file!);
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingPatternsAndEmptyForwardSearchWorkWithoutTheRuntime(int optimization)
    {
        string ir = Compiler.Emit(Sources("""
            using Standard::Text;

            int main() nothrow
            {
                char[5] data = ['a', 'a', 'a', 'a', '\0'];
                if (CString::IndexOf(&data[0], &data[1]) != 0 || CString::LastIndexOf(&data[0], &data[1]) != 1) return 1;
                if (!CString::Contains(&data[0], 'a') || CString::Contains(&data[0], 'z') || CString::Contains(&data[0], '\0')) return 2;
                if (!CString::Contains(&data[0], &data[1]) || !CString::Contains("", "")) return 3;
                readonly(char)* unreadable = ((nuint)1).reinterpret<readonly(char)*>();
                if (CString::IndexOf(unreadable, "") != 0 || !CString::Contains(unreadable, "")) return 4;
                if (CString::LastIndexOf(&data[0], "") != 4 || CString::LastIndexOf("", "") != 0) return 5;
                return 0;
            }
            """), options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
