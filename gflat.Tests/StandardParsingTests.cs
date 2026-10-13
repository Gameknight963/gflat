namespace gflat.Tests;

public class StandardParsingTests
{
    private static ExecutionResult Run(string source) => CompilerTestHelper.Run(new[] {
        new SourceFile("Parsing.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Parsing.gf"))),
        new SourceFile("main.gf", source)
    });

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("-0", true, 0)]
    [InlineData("+42", true, 42)]
    [InlineData("000123", true, 123)]
    [InlineData("-123", true, -123)]
    [InlineData("2147483647", true, int.MaxValue)]
    [InlineData("-2147483648", true, int.MinValue)]
    [InlineData("2147483648", false, 0)]
    [InlineData("-2147483649", false, 0)]
    [InlineData("999999999999999999999999999999999", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("+", false, 0)]
    [InlineData("-", false, 0)]
    [InlineData(" 1", false, 0)]
    [InlineData("1 ", false, 0)]
    [InlineData("1x", false, 0)]
    [InlineData("1.0", false, 0)]
    [InlineData("0x10", false, 0)]
    [InlineData("--1", false, 0)]
    public void ParsesOnlyCompleteDecimalIntegers(string text, bool success, int expected)
    {
        var result = Run($$"""
            using std;
            int main() {
                int value = 99;
                bool success = TryParseInt("{{text}}", &value);
                if (success != {{(success ? "true" : "false")}}) return 1;
                if ((long)value != {{expected}}l) return 2;
                value = 99;
                success = TryParseInt("{{text}}", {{text.Length}}, &value);
                if (success != {{(success ? "true" : "false")}}) return 3;
                if ((long)value != {{expected}}l) return 4;
                bool caught = false;
                try {
                    value = ParseInt("{{text}}");
                    if ((long)value != {{expected}}l) return 5;
                } catch { caught = true; }
                if (caught == {{(success ? "true" : "false")}}) return 6;
                caught = false;
                try {
                    value = ParseInt("{{text}}", {{text.Length}});
                    if ((long)value != {{expected}}l) return 7;
                } catch { caught = true; }
                if (caught == {{(success ? "true" : "false")}}) return 8;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void CountedInputNeedsNoTerminatorAndRejectsEmbeddedNulls()
    {
        var result = Run("""
            using std;
            int main() {
                char[3] buffer = ['1', '2', 'x'];
                int value = 99;
                if (!TryParseInt(buffer, 2, &value) || value != 12) return 1;
                if (TryParseInt(buffer, 3, &value) || value != 0) return 2;
                if (TryParseInt("1\0x", 3, &value) || value != 0) return 3;
                if (!TryParseInt("1\0x", &value) || value != 1) return 4;
                if (ParseInt(buffer, 2) != 12) return 5;
                if (ParseInt("1\0x") != 1) return 6;
                bool caught = false;
                try { ParseInt("1\0x", 3); } catch { caught = true; }
                if (!caught) return 7;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }
}
