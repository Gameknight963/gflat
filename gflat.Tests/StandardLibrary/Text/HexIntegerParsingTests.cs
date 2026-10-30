using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class HexIntegerParsingTests
{
    private static readonly (string Name, string Type, ulong Maximum)[] Widths =
    [
        ("Byte", "byte", byte.MaxValue), ("UShort", "ushort", ushort.MaxValue),
        ("UInt", "uint", uint.MaxValue), ("ULong", "ulong", ulong.MaxValue),
        ("NUInt", "nuint", nuint.MaxValue)
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictCountedAndCStringParsingMatchUnsignedHexOracleAtEveryWidth(int optimization)
    {
        var random = new Random(99831);
        var code = new StringBuilder("using std; using std::Text; int main() {\n");
        foreach (var (name, type, maximum) in Widths)
        {
            List<string> cases = ["", "0", "f", "F", "0x0", "0XAf", "+F", "-0", "0x", "0X",
                "0xx1", " 1", "1 ", "0x+1", "0x-1", "1_0", "1g", "g1", "00x1", "1\0f",
                "000000000000000000000000000000000f", maximum.ToString("x", CultureInfo.InvariantCulture),
                ((BigInteger)maximum + 1).ToString("x", CultureInfo.InvariantCulture), maximum.ToString("x", CultureInfo.InvariantCulture) + "0"];
            for (int sample = 0; sample < 24; sample++)
            {
                byte[] bytes = new byte[8]; random.NextBytes(bytes);
                ulong value = BitConverter.ToUInt64(bytes) & maximum;
                string text = value.ToString(sample % 2 == 0 ? "x" : "X", CultureInfo.InvariantCulture);
                if (sample % 3 == 0) text = "0x" + text;
                if (sample % 5 == 0) text += "z";
                cases.Add(text);
            }
            foreach (string input in cases)
            {
                string digits = input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? input[2..] : input;
                bool success = ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value) && value <= maximum;
                if (!success) value = 0;
                string cstring = input.Split('\0')[0];
                string cDigits = cstring.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? cstring[2..] : cstring;
                bool cSuccess = ulong.TryParse(cDigits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong cValue) && cValue <= maximum;
                if (!cSuccess) cValue = 0;
                code.AppendLine($$"""
                    {
                        {{type}} value = ({{type}})7;
                        if (TryParseHex{{name}}({{Literal(input)}}, {{input.Length}}, &value) != {{Boolean(success)}} || value != ({{type}}){{value}}ul) return 1;
                        value = ({{type}})7;
                        if (TryParseHex{{name}}({{Literal(input)}}, &value) != {{Boolean(cSuccess)}} || value != ({{type}}){{cValue}}ul) return 2;
                    }
                    """);
            }
            code.AppendLine($$"""
                {
                    {{type}} value = ({{type}})0;
                    char[5] bytes = ['0', 'x', 'F', 'f', (char)255];
                    if (!TryParseHex{{name}}(bytes, 4, &value) || value != ({{type}})255) return 3;
                    if (TryParseHex{{name}}(bytes, 5, &value) || value != ({{type}})0 || bytes[4] != (char)255) return 4;
                    if (ParseHex{{name}}("0x{{maximum:x}}") != ({{type}}){{maximum}}ul || ParseHex{{name}}(bytes, 4) != ({{type}})255) return 5;
                    int caught = 0;
                    try { ParseHex{{name}}("0x"); } catch (ref Exception error) { caught++; }
                    try { ParseHex{{name}}("1z", 2); } catch (ref Exception error) { caught++; }
                    try { ParseHex{{name}}("{{maximum:x}}0"); } catch (ref Exception error) { caught++; }
                    if (caught != 3) return 6;
                }
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OutputsMayOverlapAlignedInputAndZeroLengthNeverReadsInput(int optimization)
    {
        var code = new StringBuilder("using std; using std::Text; int main() {\n");
        foreach (var (name, type, _) in Widths)
            code.AppendLine($$"""
                {
                    ulong[4] storage = [0ul, 0ul, 0ul, 0ul];
                    char* text = (&storage[0]).reinterpret<char*>();
                    {{type}}* output = (&storage[0]).reinterpret<{{type}}*>();
                    text[0] = '0'; text[1] = 'x'; text[2] = 'a'; text[3] = 'F'; text[4] = '\0';
                    if (!TryParseHex{{name}}(text, 4, output) || *output != ({{type}})175) return 1;
                    text[0] = 'F'; text[1] = 'f'; text[2] = '\0';
                    if (!TryParseHex{{name}}(text, output) || *output != ({{type}})255) return 2;
                    text[0] = '0'; text[1] = 'x'; text[2] = '1'; text[3] = 'g'; text[4] = '\0';
                    if (TryParseHex{{name}}(text, 4, output) || *output != ({{type}})0) return 3;
                    *output = ({{type}})7;
                    if (TryParseHex{{name}}(((nuint)1).reinterpret<readonly(char)*>(), 0, output) || *output != ({{type}})0) return 4;
                    text[0] = '0'; text[1] = 'x';
                    if (!TryParseHex{{name}}(text, 1, output) || *output != ({{type}})0) return 5;
                }
                """);
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TryParsersAreAllocationFreeNothrowAndAcceptLongLeadingZeroes(int optimization)
        => Run("""
            using std; using std::Text;
            namespace Memory {
                replace void*? Allocate(nuint count) => null;
                replace void Free(void* storage) nothrow {}
            }
            bool Probe(readonly(char)* input, nuint count) nothrow {
                byte small = 0;
                ushort medium = 0;
                uint ordinary = 0;
                ulong wide = 0ul;
                nuint native = 0;
                return TryParseHexByte(input, count, &small) && TryParseHexUShort(input, count, &medium) &&
                    TryParseHexUInt(input, count, &ordinary) && TryParseHexULong(input, count, &wide) &&
                    TryParseHexNUInt(input, count, &native) && small == (byte)255 && medium == (ushort)255 && ordinary == 255u && wide == 255ul && native == (nuint)255;
            }
            int main() {
                char[8192] input;
                for (nuint i = 0; i < 8190; i++) input[i] = '0';
                input[8190] = 'F'; input[8191] = 'f';
                if (!Probe(input, 8192)) return 1;
                return 0;
            }
            """, optimization);

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Literal(string value) => "\"" + value.Replace("\0", "\\0") + "\"";

    private static void Run(string code, int optimization)
    {
        var sources = new[] { "Text/hex_integer_parsing.gf", "Text/hex_integer_parsing_throwing.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}
