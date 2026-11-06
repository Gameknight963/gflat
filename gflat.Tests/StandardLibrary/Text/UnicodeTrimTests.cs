using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class UnicodeTrimTests
{
    private const string Imports = "using Standard;\nusing Standard::Text;\n\n";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndCStringTrimmingMatchDotNetForUnicodeWhitespaceAndPreservedScalars(int optimization)
    {
        const string spaces = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
        string[] cases = ["", spaces, spaces + "value" + spaces, "\u2003😀\u00a0", "no trim", " \0A\0 ",
            "\ufeff  x  \ufeff", "\u200bx\u200b", " \ufeffx ", " x \0ignored \u2003"];
        var globals = new StringBuilder(Imports);
        var body = new StringBuilder("int main() {\n");
        int id = 0;
        foreach (string text in cases)
        {
            byte[] input = Encoding.UTF8.GetBytes(text);
            globals.Append(Bytes("Input" + id, [.. input, 0]));
            int zero = text.IndexOf('\0');
            string cText = zero < 0 ? text : text[..zero];
            foreach (string operation in new[] { "Trim", "TrimStart", "TrimEnd" })
            {
                foreach (bool counted in new[] { true, false })
                {
                    string value = counted ? text : cText;
                    string expected = operation switch { "Trim" => value.Trim(), "TrimStart" => value.TrimStart(), _ => value.TrimEnd() };
                    byte[] bytes = Encoding.UTF8.GetBytes(expected);
                    string name = $"Expected{id}_{operation}_{counted}";
                    globals.Append(Bytes(name, bytes));
                    body.AppendLine($$"""
                        {
                            readonly(byte)* source = Input{{id}};
                            readonly(char)* text = source.reinterpret<readonly(char)*>();
                            String result = UnicodeText::{{operation}}(text{{(counted ? ", " + input.Length : "")}});
                            if (result.Length != {{bytes.Length}} || result.Data[result.Length] != '\0') return 1;
                            if (!result.IsEmpty && result.Data == text) return 2;
                            for (nuint index = 0; index < result.Length; index++)
                                if ((byte)result.Data[index] != {{name}}[index]) return 3;
                        }
                        """);
                }
            }
            id++;
        }
        body.AppendLine("return 0; }");
        Run(globals.Append(body).ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullValidationPrecedesResultAllocationAndOwningResultsCleanUpOnFailure(int optimization)
        => Run(Imports + """
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* data);
            static class State { public static bool Failing; public static int Calls; public static int Large; public static int Live; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    State::Calls++;
                    if (bytes >= 4096) State::Large++;
                    if (State::Failing) return null;
                    void*? result = malloc(bytes);
                    if (result != null) State::Live++;
                    return result;
                }
                public replace void Free(void* data) nothrow { State::Live--; free(data); }
            }
            int main() {
                {
                    char[8192] invalid;
                    for (nuint index = 0; index < 8192; index++) invalid[index] = 'x';
                    invalid[4096] = (char)255; invalid[8191] = '\0';
                    int large = State::Large;
                    try { String result = UnicodeText::Trim(invalid, 8191); return 1; } catch (ref Exception error) {}
                    try { String result = UnicodeText::TrimStart(invalid, 8191); return 2; } catch (ref Exception error) {}
                    try { String result = UnicodeText::TrimEnd(invalid); return 3; } catch (ref Exception error) {}
                    if (State::Large != large || (byte)invalid[4096] != (byte)255 || invalid[0] != 'x') return 4;
                    invalid[4096] = 'x';
                    for (int variant = 0; variant < 2; variant++) {
                        byte final = 255;
                        if (variant == 1) final = 0xe2;
                        invalid[8190] = (char)final;
                        try { String trimmed = UnicodeText::Trim(invalid, 8191); return 13; } catch (ref Exception error) {}
                        try { String trimmed = UnicodeText::TrimStart(invalid, 8191); return 14; } catch (ref Exception error) {}
                        try { String trimmed = UnicodeText::TrimEnd(invalid, 8191); return 15; } catch (ref Exception error) {}
                        if (State::Large != large || (byte)invalid[8190] != final || invalid[0] != 'x' || invalid[4096] != 'x') return 16;
                    }
                    invalid[0] = ' '; invalid[1] = 'v'; invalid[2] = ' '; invalid[3] = '\0'; invalid[4096] = (char)255;
                    {
                        String both = UnicodeText::Trim(invalid);
                        String start = UnicodeText::TrimStart(invalid);
                        String end = UnicodeText::TrimEnd(invalid);
                        if (!both.Equals("v") || !start.Equals("v ") || !end.Equals(" v")) return 17;
                        try { String trimmed = UnicodeText::Trim(invalid, 8191); return 18; } catch (ref Exception error) {}
                        try { String trimmed = UnicodeText::TrimStart(invalid, 8191); return 19; } catch (ref Exception error) {}
                        try { String trimmed = UnicodeText::TrimEnd(invalid, 8191); return 20; } catch (ref Exception error) {}
                        if (State::Large != large) return 21;
                        for (nuint index = 0; index < 8192; index++) {
                            char expected = 'x';
                            if (index == 0 || index == 2) expected = ' ';
                            if (index == 1) expected = 'v';
                            if (index == 3 || index == 8191) expected = '\0';
                            if (index == 4096) expected = (char)255;
                            if (index == 8190) expected = (char)0xe2;
                            if (invalid[index] != expected) return 22;
                        }
                    }
                    String source = String::From("  value  "); // U+2003 and U+00A0 surround value.
                    String result = UnicodeText::Trim(source.Data, source.Length);
                    source[4] = 'X';
                    if (!result.Equals("value")) return 5;
                    String destination = String::From("unchanged");
                    readonly(char)* data = destination.Data;
                    nuint capacity = destination.Capacity;
                    State::Failing = true;
                    int calls = State::Calls;
                    String empty = UnicodeText::Trim("");
                    String all = UnicodeText::Trim("  ");
                    String allStart = UnicodeText::TrimStart("  ");
                    String allEnd = UnicodeText::TrimEnd("  ");
                    if (State::Calls != calls || !empty.IsEmpty || !all.IsEmpty || !allStart.IsEmpty || !allEnd.IsEmpty) return 6;
                    try { destination = UnicodeText::Trim(source.Data); return 7; } catch (BadAllocException* error) {}
                    try { destination = UnicodeText::TrimStart(source.Data); return 8; } catch (BadAllocException* error) {}
                    try { destination = UnicodeText::TrimEnd(source.Data); return 9; } catch (BadAllocException* error) {}
                    State::Failing = false;
                    if (!destination.Equals("unchanged") || destination.Data != data || destination.Capacity != capacity) return 10;
                    if (!source.Equals("  Xalue  ")) return 11;
                }
                if (State::Live != 0) return 12;
                return 0;
            }
            """, optimization);

    private static string Bytes(string name, byte[] bytes)
        => $"const byte[{bytes.Length}] {name} = [" + string.Join(",", bytes.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "];\n";

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/Text/UnicodeText.gf", "core/Text/UnicodeTranscoding.gf", "core/Text/utf8.gf", "core/Text/utf16.gf",
            "core/Text/Rune.gf", "core/Text/CString.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf",
            "core/Memory/Span.gf", "core/Collections/Vector.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf" }
            .Select(path => new SourceFile(path, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", path))))
            .Append(new SourceFile("unicode-trim.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
