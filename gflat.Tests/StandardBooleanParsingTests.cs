using System.Text;

namespace gflat.Tests;

public sealed class StandardBooleanParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryAsciiCaseCombinationAndStrictInvalidCorpusUseCountedAndCStringForms(int optimization)
    {
        var cases = new List<string>();
        foreach (string word in new[] { "true", "false" })
            for (int mask = 0; mask < 1 << word.Length; mask++)
                cases.Add(new string(word.Select((letter, index) => (mask & (1 << index)) == 0 ? letter : char.ToUpperInvariant(letter)).ToArray()));
        cases.AddRange(["", "t", "tru", "fals", "trues", "FALSES", "0", "1", "+true", "-false", " true", "false ",
            "\ttrue", "false\r\n", "t_rue", "fals_e", "truth", "null", "true\0suffix", "false\0", "tr\0ue", "fa\0lse"]);
        var code = new StringBuilder("using std; int main() {\n");
        foreach (string text in cases)
        {
            bool valid = Strict(text, out bool value);
            bool cValid = Strict(text.Split('\0')[0], out bool cValue);
            code.AppendLine($$"""
                {
                    bool value = true;
                    if (TryParseBool({{Literal(text)}}, {{text.Length}}, &value) != {{Bool(valid)}} || value != {{Bool(value)}}) return 1;
                    value = true;
                    if (TryParseBool({{Literal(text)}}, &value) != {{Bool(cValid)}} || value != {{Bool(cValue)}}) return 2;
                    bool caught = false;
                    try { value = ParseBool({{Literal(text)}}, {{text.Length}}); if (value != {{Bool(value)}}) return 3; }
                    catch (ref Exception error) { caught = true; }
                    if (caught == {{Bool(valid)}}) return 4;
                    caught = false;
                    try { value = ParseBool({{Literal(text)}}); if (value != {{Bool(cValue)}}) return 5; }
                    catch (ref Exception error) { caught = true; }
                    if (caught == {{Bool(cValid)}}) return 6;
                }
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteMutationRespectsAsciiOnlyMatchingAndCountedGuards(int optimization)
        => Run("""
            using std;
            int main() {
                char[7] buffer;
                for (int word = 0; word < 2; word++) {
                    readonly(char)* expected = "true";
                    nuint count = 4;
                    if (word != 0) { expected = "false"; count = 5; }
                    for (nuint at = 0; at < count; at++) {
                        for (int value = 0; value < 256; value++) {
                            buffer[0] = (char)165; buffer[6] = (char)90;
                            for (nuint i = 0; i < count; i++) buffer[i + 1] = expected[i];
                            buffer[count + 1] = (char)90;
                            buffer[at + 1] = (char)value;
                            bool matches = (char)value == expected[at] || (char)value == (char)(expected[at] - 'a' + 'A');
                            bool parsed = true;
                            if (TryParseBool(&buffer[1], count, &parsed) != matches || parsed != (matches && word == 0)) return 1;
                            if (buffer[0] != (char)165 || buffer[count + 1] != (char)90 || buffer[6] != (char)90) return 2;
                            for (nuint i = 0; i < count; i++) {
                                char original = expected[i]; if (i == at) original = (char)value;
                                if (buffer[i + 1] != original) return 3;
                            }
                        }
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingOutputsAndZeroLengthAreAllocationFreeNothrow(int optimization)
        => Run("""
            using std;
            namespace Memory {
                replace void*? Allocate(nuint count) => null;
                replace void Free(void* storage) {}
            }
            bool Probe(char* text, nuint count, bool* result) nothrow => TryParseBool(text, count, result);
            int main() {
                ulong[1] storage = [0ul];
                char* text = (char*)&storage[0]; bool* result = (bool*)&storage[0];
                for (nuint i = 0; i < 5; i++) text[i] = "TrUe"[i];
                if (!Probe(text, 4, result) || !*result) return 1;
                for (nuint i = 0; i < 6; i++) text[i] = "fAlSe"[i];
                if (!TryParseBool(text, result) || *result) return 2;
                for (nuint i = 0; i < 6; i++) text[i] = "falsX"[i];
                if (Probe(text, 5, result) || *result) return 3;
                *result = true;
                if (TryParseBool((readonly(char)*)1, 0, result) || *result) return 4;
                *result = true;
                if (TryParseBool((readonly(char)*)1, (nuint)0 - (nuint)1, result) || *result) return 5;
                return 0;
            }
            """, optimization);

    private static bool Strict(string text, out bool value)
    {
        bool valid = text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("false", StringComparison.OrdinalIgnoreCase);
        value = valid && bool.Parse(text);
        return valid;
    }
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Literal(string value) => "\"" + value.Replace("\0", "\\0").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    private static void Run(string code, int optimization)
    {
        var sources = new[] { "BooleanParsing.gf", "BooleanParsingThrowing.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}
