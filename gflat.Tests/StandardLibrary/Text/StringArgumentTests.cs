namespace gflat.Tests.Std.Text;

public sealed class StringArgumentTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("STRING_ARGUMENTS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedStringArgumentsMatchOrdinalDotNetOperations(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_string_args_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(918274);
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int row = 0; row < 128; row++)
            {
                string input = new(Enumerable.Range(0, row % 17).Select(_ => (char)random.Next(0, 4)).ToArray());
                string pattern = new(Enumerable.Range(0, row % 5).Select(_ => (char)random.Next(0, 4)).ToArray());
                string replacement = new(Enumerable.Range(0, row % 4).Select(_ => (char)random.Next(0, 4)).ToArray());
                int start = row % (input.Length + 2);
                int first = input.IndexOf(pattern, StringComparison.Ordinal), last = input.LastIndexOf(pattern, StringComparison.Ordinal);
                int fromStart = start > input.Length ? -2 : input.IndexOf(pattern, start, StringComparison.Ordinal);
                string changed = pattern.Length == 0 ? input : input.Replace(pattern, replacement, StringComparison.Ordinal);
                long[] header = [input.Length, pattern.Length, replacement.Length, first, last,
                    input.StartsWith(pattern, StringComparison.Ordinal) ? 1 : 0,
                    input.EndsWith(pattern, StringComparison.Ordinal) ? 1 : 0, input.Contains(pattern, StringComparison.Ordinal) ? 1 : 0,
                    start, fromStart, pattern.Length == 0 ? 0 : 1, changed.Length];
                foreach (long value in header) writer.Write(value);
                foreach (string value in new[] { input, pattern, replacement, changed }) writer.Write(System.Text.Encoding.Latin1.GetBytes(value));
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Values.Append(new SourceFile("string-args.gf", $$"""
                using Standard;
                using Standard::Text;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int row = 0; row < 128; row++)
                    {
                        long[12] header = default(long[12]);
                        char[128] data = default(char[128]);
                        if (fread(&header[0], 8, 12, file!) != 12) return 2;
                        if (fread(&data[0], 1, (nuint)header[0], file!) != (nuint)header[0]) return 3;
                        String input = String::From(&data[0], (nuint)header[0]);
                        if (fread(&data[0], 1, (nuint)header[1], file!) != (nuint)header[1]) return 3;
                        String pattern = String::From(&data[0], (nuint)header[1]);
                        if (fread(&data[0], 1, (nuint)header[2], file!) != (nuint)header[2]) return 3;
                        String replacement = String::From(&data[0], (nuint)header[2]);
                        if (fread(&data[0], 1, (nuint)header[11], file!) != (nuint)header[11]) return 3;
                        if (input.IndexOf(&pattern) != (nuint)header[3] || input.LastIndexOf(&pattern) != (nuint)header[4]) return 4;
                        if (input.StartsWith(&pattern) != (header[5] == 1l) || input.EndsWith(&pattern) != (header[6] == 1l) || input.Contains(&pattern) != (header[7] == 1l)) return 5;
                        nuint index = 123;
                        bool caught = false;
                        try { index = input.IndexOf(&pattern, (nuint)header[8]); }
                        catch (Exception* error) { caught = true; }
                        if (header[9] == -2l) { if (!caught || index != 123) return 6; }
                        else if (caught || index != (nuint)header[9]) return 7;
                        caught = false;
                        try { input.Replace(&pattern, &replacement); }
                        catch (Exception* error) { caught = true; }
                        if (caught != (header[10] == 0l) || !input.Equals(&data[0], (nuint)header[11])) return 8;
                    }
                    String aliased = s"aba";
                    String a = s"a";
                    aliased.Replace(&a, &aliased);
                    if (!aliased.Equals("abababa")) return 9;
                    aliased.Replace(&aliased, &a);
                    if (!aliased.Equals("a")) return 10;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"String args exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
