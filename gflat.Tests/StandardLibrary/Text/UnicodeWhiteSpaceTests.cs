using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class UnicodeWhiteSpaceTests
{
    private const string Prelude = """
        using Standard::Text;

        extern void*? malloc(nuint count);
        extern void free(void* storage);
        int attempts = 0;
        int live = 0;
        bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;
                if (deny) return null;
                void*? result = malloc(count);
                if (result != null) live++;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidUnicodeAndCStringPrefixesMatchDotNetWithoutAllocating(int optimization)
    {
        const string spaces = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
        var cases = new List<string> { "", spaces, "plain", spaces + "x", "x" + spaces, "\u2003😀\u00a0",
            "\ufeff", "\u180e", "\u200b", "\0", " \0ignored", "\0\u2003", spaces + "\0" + spaces };
        foreach (char value in spaces) cases.Add(value.ToString());
        var random = new Random(62881);
        for (int sample = 0; sample < 24; sample++)
        {
            var text = new StringBuilder();
            for (int index = 0; index < random.Next(1, 12); index++)
            {
                if (sample % 2 == 0) text.Append(spaces[random.Next(spaces.Length)]);
                else
                {
                    int scalar;
                    do { scalar = random.Next(0x110000); } while (!System.Text.Rune.IsValid(scalar));
                    text.Append(new System.Text.Rune(scalar).ToString());
                }
            }
            cases.Add(text.ToString());
        }
        var globals = new StringBuilder(Prelude);
        var body = new StringBuilder("int main() nothrow { deny = true; int before = attempts;\n");
        body.AppendLine("if (!UnicodeText::IsNullOrWhiteSpace(null)) return 1;");
        body.AppendLine("if (!UnicodeText::IsWhiteSpace(((nuint)1).reinterpret<readonly(char)*>(), 0)) return 2;");
        for (int index = 0; index < cases.Count; index++)
        {
            string text = cases[index];
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            globals.Append(Bytes("Input" + index, [.. bytes, 0]));
            int zero = text.IndexOf('\0');
            string prefix = zero < 0 ? text : text[..zero];
            body.AppendLine($$"""
                {
                    readonly(byte)* source = Input{{index}};
                    readonly(char)* text = source.reinterpret<readonly(char)*>();
                    if (UnicodeText::IsWhiteSpace(text, {{bytes.Length}}) != {{Boolean(string.IsNullOrWhiteSpace(text))}}) return 3;
                    if (UnicodeText::IsNullOrWhiteSpace(text) != {{Boolean(string.IsNullOrWhiteSpace(prefix))}}) return 4;
                }
                """);
        }
        body.AppendLine("if (attempts != before) return 5; return 0; }");
        Run(globals.Append(body).ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidUtf8AndCountedNulsReturnFalseAndLeaveCallerOwnersUnchanged(int optimization)
    {
        byte[][] malformed = [
            [0x80], [0xff], [0xc0, 0x80], [0xc2], [0xe2, 0x80], [0xf0, 0x90, 0x80],
            [0xed, 0xa0, 0x80], [0xf4, 0x90, 0x80, 0x80], [0xf5, 0x80, 0x80, 0x80],
            [0x20, 0xe2, 0x28, 0xa1], [0x20, 0, 0xff], [0xe2, 0, 0xa0], [0, 0xff], [0x78, 0xff]
        ];
        var strict = new UTF8Encoding(false, true);
        var globals = new StringBuilder(Prelude);
        var body = new StringBuilder("int main() { deny = true; int before = attempts;\n");
        for (int index = 0; index < malformed.Length; index++)
        {
            byte[] bytes = malformed[index];
            globals.Append(Bytes("Input" + index, [.. bytes, 0]));
            int zero = Array.IndexOf(bytes, (byte)0);
            ReadOnlySpan<byte> prefix = bytes.AsSpan(0, zero < 0 ? bytes.Length : zero);
            bool expectedCString;
            try { expectedCString = string.IsNullOrWhiteSpace(strict.GetString(prefix)); }
            catch (DecoderFallbackException) { expectedCString = false; }
            body.AppendLine($$"""
                {
                    readonly(byte)* source = Input{{index}};
                    readonly(char)* text = source.reinterpret<readonly(char)*>();
                    if (UnicodeText::IsWhiteSpace(text, {{bytes.Length}})) return 1;
                    if (UnicodeText::IsNullOrWhiteSpace(text) != {{Boolean(expectedCString)}}) return 2;
                }
                """);
        }
        body.AppendLine("""
                if (attempts != before) return 3;
                deny = false;
                {
                    char[6] original = [' ', (char)0xe2, (char)0x80, (char)0x83, (char)0xc2, (char)0xa0];
                    String owner = String::From(original, 6);
                    readonly(char)* identity = owner.Data;
                    nuint capacity = owner.Capacity;
                    before = attempts; deny = true;
                    if (!UnicodeText::IsWhiteSpace(owner.Data, owner.Length) || !UnicodeText::IsNullOrWhiteSpace(owner.Data)) return 4;
                    if (owner.Data != identity || owner.Capacity != capacity || owner.Length != 6) return 5;
                    for (nuint index = 0; index < 6; index++) if (owner.Data[index] != original[index]) return 6;
                    owner[4] = (char)255;
                    if (UnicodeText::IsWhiteSpace(owner.Data, owner.Length) || UnicodeText::IsNullOrWhiteSpace(owner.Data)) return 7;
                    if (attempts != before || owner.Data != identity || owner.Length != 6 || owner.Capacity != capacity) return 8;
                    for (nuint index = 0; index < 6; index++) {
                        char expected = original[index]; if (index == 4) expected = (char)255;
                        if (owner.Data[index] != expected) return 9;
                    }
                    deny = false;
                }
                if (live != 0) return 10;
                return 0;
            }
            """);
        Run(globals.Append(body).ToString(), optimization);
    }

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string Bytes(string name, byte[] bytes)
        => $"const byte[{bytes.Length}] {name} = [" + string.Join(",", bytes.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "];\n";

    private static void Run(string code, int optimization)
    {
        string[] names = ["core/Text/UnicodeText.gf", "core/Text/UnicodeTranscoding.gf", "core/Text/utf8.gf", "core/Text/utf16.gf",
            "core/Text/Rune.gf", "core/Text/CString.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf",
            "core/Memory/Span.gf", "core/Collections/Vector.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("unicode-whitespace.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
