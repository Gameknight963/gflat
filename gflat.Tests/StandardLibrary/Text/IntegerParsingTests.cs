using System.Numerics;

namespace gflat.Tests.Std.Text;

public class IntegerParsingTests
{
    private static IEnumerable<SourceFile> Sources(string code, bool strings = false) =>
        new[] { "Text/integer_parsing.gf" }
            .Concat(strings ? ["Hashing/primitive_hashing.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "IStringConvertable.gf", "Text/String.gf", "Text/ByteSearch.gf", "Text/string_integer_parsing.gf"] : [])
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("main.gf", code));

    public static TheoryData<string, string, string, string> Widths => new()
    {
        { "Byte", "byte", "0", "255" },
        { "SByte", "sbyte", "-128", "127" },
        { "Short", "short", "-32768", "32767" },
        { "UShort", "ushort", "0", "65535" },
        { "UInt", "uint", "0", "4294967295" },
        { "Long", "long", "-9223372036854775808", "9223372036854775807" },
        { "ULong", "ulong", "0", "18446744073709551615" },
        { "NInt", "nint", nint.MinValue.ToString(), nint.MaxValue.ToString() },
        { "NUInt", "nuint", "0", nuint.MaxValue.ToString() },
    };

    [Fact]
    public void LongLeadingZeroInputDoesNotOverflowOrAccumulateStackStorage()
    {
        Assert.Equal(0, CompilerTestHelper.Run(Sources("""
            using Standard; using Standard::Text; using Standard::Numerics;
            int main() {
                char[131072] text;
                for (nuint i = 0; i < (nuint)131072; i++) text[i] = '0';
                text[131071] = '7';
                ulong value = 0ul;
                if (!TryParseULong(text, 131072, &value)) return 1;
                if (value != 7ul) return 2;
                text[131071] = 'x';
                if (TryParseULong(text, 131072, &value)) return 3;
                if (value != 0ul) return 4;
                return 0;
            }
            """)).ExitCode);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void ExactLimitsAndMalformedInputRespectEachWidth(string name, string type, string minimum, string maximum)
    {
        var min = BigInteger.Parse(minimum);
        var max = BigInteger.Parse(maximum);
        string expectedMin = min < 0 ? $"0ul - {BigInteger.Abs(min)}ul" : "0ul";
        var invalid = new[] { "", "+", "-", " 1", "1 ", "1x", "0x10", "1_0", "++1", "+-1", (max + 1).ToString(), (min - 1).ToString(), "99999999999999999999999999999999999" };
        if (min == 0) invalid = invalid.Append("-0").ToArray();
        string failures = string.Join("\n", invalid.Select((text, i) =>
            $"value = ({type})7; if (TryParse{name}(\"{text}\", &value) || value != ({type})0) return {10 + i};"));
        string code = $$"""
            using Standard; using Standard::Text; using Standard::Numerics;
            int main() {
                {{type}} value = ({{type}})0;
                if (!TryParse{{name}}("{{minimum}}", &value) || value != ({{type}})({{expectedMin}})) return 1;
                if (!TryParse{{name}}("{{maximum}}", &value) || value != ({{type}}){{maximum}}ul) return 2;
                if (Parse{{name}}("{{minimum}}") != ({{type}})({{expectedMin}})) return 3;
                if (Parse{{name}}("{{maximum}}") != ({{type}}){{maximum}}ul) return 4;
                if (!TryParse{{name}}("+00012", &value) || value != ({{type}})12) return 5;
                if (!TryParse{{name}}("12garbage", 2, &value) || value != ({{type}})12) return 6;
                if (TryParse{{name}}("12\0", 3, &value) || value != ({{type}})0) return 7;
                {{failures}}
                try { Parse{{name}}("{{max + 1}}"); return 40; } catch (ref Exception error) {}
                try { Parse{{name}}("1x", 2); return 41; } catch (ref Exception error) {}
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Sources(code)).ExitCode);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void OwningStringConveniencesUseCountedInput(string name, string type, string minimum, string maximum)
    {
        string code = $$"""
            using Standard; using Standard::Text; using Standard::Numerics;
            int main() {
                String text = s"{{maximum}}";
                {{type}} result = ({{type}})0;
                if (!text.TryParse{{name}}(&result) || result != ({{type}}){{maximum}}ul) return 1;
                if (text.Parse{{name}}() != result) return 2;
                String smallest = s"{{minimum}}";
                if (!smallest.TryParse{{name}}(&result) || smallest.Parse{{name}}() != result) return 5;
                String bad = String::From("12\0", 3);
                if (bad.TryParse{{name}}(&result) || result != ({{type}})0) return 3;
                try { bad.Parse{{name}}(); return 4; } catch (ref Exception error) {}
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Sources(code, strings: true)).ExitCode);
    }
}
