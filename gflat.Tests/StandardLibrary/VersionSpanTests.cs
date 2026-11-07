using System.Text;

namespace gflat.Tests.Std;

public sealed class VersionSpanTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Version.gf"))
            .Append(new SourceFile("version-spans.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Version span exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("VERSION_SPANS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SlicedTextMatchesDotNetAndFormatsWithoutAllocationAtEveryCapacity(int optimization)
    {
        var inputs = new List<string> { "0.0", "1.2", "0001.0002", "1.2.0", "1.2.3.0",
            "2147483647.2147483647.2147483647.2147483647" };
        var random = new Random(774205);
        for (int i = 0; i < 128; i++)
            inputs.Add(string.Join('.', Enumerable.Range(0, 2 + i % 3).Select(_ => random.Next())));
        var checks = new StringBuilder();
        foreach (string input in inputs)
        {
            Version value = Version.Parse(input);
            checks.AppendLine($$"""
                if (Check("{{input}}", {{input.Length}}, "{{value}}", {{value.Major}}, {{value.Minor}}, {{value.Build}}, {{value.Revision}})) return 1;
                {
                    Version parsed = Version::Parse(new Span<readonly(char)>("{{input}}", {{input.Length}}));
                    if (parsed.Major != {{value.Major}} || parsed.Minor != {{value.Minor}} || parsed.Build != {{value.Build}} || parsed.Revision != {{value.Revision}}) return 2;
                }
                """);
        }
        Run(Support + """
            bool Check(readonly(char)* text, nuint count, readonly(char)* expected,
                int major, int minor, int build, int revision) nothrow
            {
                char[64] input;
                input[0] = '#';
                for (nuint i = 0; i < count; i++) input[i + 1] = text[i];
                input[count + 1] = '#';
                Version value = new();
                var view = new Span<readonly(char)>(&input[1], count);
                if (!Version::TryParse(view, &value)) return true;
                if (value.Major != major || value.Minor != minor || value.Build != build || value.Revision != revision) return true;
                nuint length = 0;
                while (expected[length] != '\0') length++;
                for (nuint capacity = 0; capacity <= length + 1; capacity++)
                {
                    char[48] output;
                    for (nuint i = 0; i < 48; i++) output[i] = '#';
                    nuint written = 999;
                    bool success = value.TryFormat(new Span<char>(&output[1], capacity), ref written);
                    if (success != (capacity >= length)) return true;
                    nuint wanted = 0;
                    if (success) wanted = length;
                    if (written != wanted) return true;
                    for (nuint i = 0; i < 48; i++)
                    {
                        char wantedByte = '#';
                        if (success && i >= 1 && i <= length) wantedByte = expected[i - 1];
                        if (output[i] != wantedByte) return true;
                    }
                }
                for (nuint i = 0; i < count; i++)
                {
                    char saved = input[i + 1];
                    input[i + 1] = '\0';
                    if (Version::TryParse(view, &value)) return true;
                    Version baseline = new();
                    if (!value.Equals(&baseline)) return true;
                    input[i + 1] = saved;
                }
                return false;
            }
            """ + $$"""
            int main()
            {
                {{checks}}
                return attempts;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndMalformedViewsResetOutputsAndParseThrows(int optimization)
        => Run(Support + """
            int main()
            {
                Version original = new(1, 2, 3, 4);
                Version value = original;
                Version baseline = new();
                if (Version::TryParse(new Span<readonly(char)>(), &value) || !value.Equals(&baseline)) return 1;
                nuint written = 999;
                if (original.TryFormat(new Span<char>(), ref written) || written != 0) return 2;
                if (!original.TryFormat(new Span<char>(), ref written, 0) || written != 0) return 3;
                for (int fields = -1; fields <= 5; fields++)
                    if (fields != 0 && (original.TryFormat(new Span<char>(), ref written, fields) || written != 0)) return 4;
                if (attempts != 0) return 5;
                for (int form = 0; form < 3; form++)
                {
                    Span<readonly(char)> text = new();
                    if (form == 1) text = new Span<readonly(char)>("1.2.2147483648", 14);
                    if (form == 2) text = new Span<readonly(char)>("1.-2", 4);
                    if (Version::TryParse(text, &value) || !value.Equals(&baseline)) return 6;
                    value = original;
                    bool caught = false;
                    try { value = Version::Parse(text); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || !value.Equals(&original)) return 7;
                }
                return 0;
            }
            """, optimization);

    private const string Support = """
        using Standard;
        using Standard::Memory;

        int attempts = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes) { attempts++; return null; }
            public replace void Free(void* storage) nothrow { }
        }
        """;
}
