namespace gflat.Tests.Std.Text;

public class AsciiTests
{
    private static IEnumerable<SourceFile> Sources(string code) =>
        [new SourceFile("Ascii.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/ascii.gf"))), new SourceFile("main.gf", code)];

    [Fact]
    public void EveryByteMatchesAsciiClassificationAndCaseRules()
    {
        var checks = new List<string>();
        for (int value = 0; value < 256; value++)
        {
            bool lower = value is >= 97 and <= 122;
            bool upper = value is >= 65 and <= 90;
            bool digit = value is >= 48 and <= 57;
            bool hex = digit || value is >= 65 and <= 70 or >= 97 and <= 102;
            var expected = new Dictionary<string, bool>
            {
                ["IsAscii"] = value < 128,
                ["IsDigit"] = digit,
                ["IsLower"] = lower,
                ["IsUpper"] = upper,
                ["IsLetter"] = lower || upper,
                ["IsLetterOrDigit"] = lower || upper || digit,
                ["IsHexDigit"] = hex,
                ["IsWhiteSpace"] = value == 32 || value is >= 9 and <= 13,
                ["IsControl"] = value < 32 || value == 127,
                ["IsPrintable"] = value is >= 32 and <= 126,
            };
            foreach (var (method, result) in expected)
                checks.Add($"if (std::Text::Ascii::{method}((char){value}) != {result.ToString().ToLowerInvariant()}) return 1;");
            checks.Add($"if ((byte)std::Text::Ascii::ToLower((char){value}) != (byte){(upper ? value + 32 : value)}) return 2;");
            checks.Add($"if ((byte)std::Text::Ascii::ToUpper((char){value}) != (byte){(lower ? value - 32 : value)}) return 3;");
            checks.Add($"digit = (byte)99; if (std::Text::Ascii::TryHexValue((char){value}, &digit) != {hex.ToString().ToLowerInvariant()}) return 4;");
            int decoded = digit ? value - 48 : value is >= 65 and <= 70 ? value - 55 : value is >= 97 and <= 102 ? value - 87 : 0;
            checks.Add($"if (digit != (byte){decoded}) return 5;");
        }
        string source = "int main() { byte digit = (byte)0;\n" + string.Join("\n", checks) + "\n return 0; }";
        Assert.Equal(0, CompilerTestHelper.Run(Sources(source)).ExitCode);
    }

    [Fact]
    public void AsciiUtilitiesCompileWithoutStandardLibraryDefinitions()
        => Compiler.Emit(Sources("int main() => (int)(byte)std::Text::Ascii::ToLower('A');"), options: new() { NoStdLib = true });
}
