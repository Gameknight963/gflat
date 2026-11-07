namespace gflat.Tests.Std.Collections;

public sealed class SpanPatternCountTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SPAN_COUNT.md", 1);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveBinarySequencesMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_pattern_count_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int length = 0; length <= 7; length++)
            for (int mask = 0; mask < 1 << length; mask++)
            for (int patternLength = 0; patternLength <= 4; patternLength++)
            for (int patternMask = 0; patternMask < 1 << patternLength; patternMask++)
            {
                int[] source = Enumerable.Range(0, length).Select(i => mask >> i & 1).ToArray();
                int[] pattern = Enumerable.Range(0, patternLength).Select(i => patternMask >> i & 1).ToArray();
                int expected = MemoryExtensions.Count<int>(source.AsSpan(), pattern.AsSpan());
                foreach (int item in new[] { length, mask, patternLength, patternMask, expected }) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Collections/EqualityComparer.gf", "core/Collections/default_equality.gf",
                    "core/Collections/span_subsequence_search.gf", "core/Collections/span_algorithms.gf", "core/Collections/span_find_run.gf"))
                .Append(new SourceFile("span-pattern-count.gf", $$"""
                using Standard;
                using Standard::Collections;
                using Standard::Memory;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Equal(immutable(int)* left, immutable(int)* right) => *left == *right;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[5] row;
                    int[7] source;
                    int[4] pattern;
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 20, file!);
                        if (received == 0) break;
                        if (received != 20) return 2;
                        for (int i = 0; i < row[0]; i++) source[i] = (row[1] >> i) & 1;
                        for (int i = 0; i < row[2]; i++) pattern[i] = (row[3] >> i) & 1;
                        Span<immutable(int)> values = new(&source[0], (nuint)row[0]);
                        Span<immutable(int)> needle = new(&pattern[0], (nuint)row[2]);
                        if (Count<int>(values, needle) != (nuint)row[4]) return 3;
                        if (Count<int>(values, needle, Equal) != (nuint)row[4]) return 4;
                    }
                    if (ferror(file!) != 0) return 5;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Pattern count exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
