namespace gflat.Tests.Std.Text;

public sealed class PatternSplitTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Text/TextPartsPattern.gf", "core/Text/text_parts_pattern_span.gf", "core/Text/string_split_pattern.gf",
                "core/Text/TextParts.gf", "core/Text/string_split.gf"))
            .Append(new SourceFile("pattern-split.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Pattern split exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TEXT_PATTERN_SPLIT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactSeparatorsMatchDotNetIncludingEmptyAndOverlappingPatterns(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_pattern_split_" + Guid.NewGuid().ToString("N"));
        try
        {
            var inputs = new List<(string, string)> { ("", ""), ("", "::"), ("abc", ""), ("aaaaa", "aa"),
                ("::a::::b::", "::"), ("ababa", "aba"), ("ab", "abc"), ("a\0b\0\0c", "\0"), ("a\0\xffb\0\xff", "\0\xff") };
            var random = new Random(819274);
            for (int row = 0; row < 256; row++)
            {
                string text = new(Enumerable.Range(0, row % 49).Select(_ => (char)random.Next(0, 7)).ToArray());
                string separator = new(Enumerable.Range(0, row % 6).Select(_ => (char)random.Next(0, 7)).ToArray());
                inputs.Add((text, separator));
            }
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (text, separator) in inputs)
            foreach (bool removeEmpty in new[] { false, true })
            {
                string[] parts = text.Split(separator, removeEmpty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
                writer.Write((uint)text.Length); writer.Write((uint)separator.Length);
                writer.Write(removeEmpty ? 1u : 0u); writer.Write((uint)parts.Length);
                writer.Write(System.Text.Encoding.Latin1.GetBytes(text)); writer.Write(System.Text.Encoding.Latin1.GetBytes(separator));
                foreach (string part in parts) { writer.Write((uint)part.Length); writer.Write(System.Text.Encoding.Latin1.GetBytes(part)); }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::Text;
                using Standard::Memory;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                bool Invalid(TextPartsPattern::Enumerator* iterator)
                {
                    try { Span<readonly(char)> part = iterator.Current; }
                    catch (Exception* error) { return true; }
                    return false;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int row = 0; row < {{inputs.Count * 2}}; row++)
                    {
                        uint[4] header = default(uint[4]);
                        char[64] input = default(char[64]);
                        char[8] separator = default(char[8]);
                        char[64] expected = default(char[64]);
                        if (fread(&header[0], 4, 4, file!) != 4) return 2;
                        if (fread(&input[0], 1, (nuint)header[0], file!) != (nuint)header[0] || fread(&separator[0], 1, (nuint)header[1], file!) != (nuint)header[1]) return 3;
                        String owner = String::From(&input[0], (nuint)header[0]);
                        Span<readonly(char)> text = new(&input[0], (nuint)header[0]);
                        Span<readonly(char)> pattern = new(&separator[0], (nuint)header[1]);
                        var first = text.Split(pattern, header[2] == 1u).GetEnumerator();
                        var second = owner.Split(&separator[0], (nuint)header[1], header[2] == 1u).GetEnumerator();
                        if (!Invalid(&first) || !Invalid(&second)) return 4;
                        for (nuint part = 0; part < (nuint)header[3]; part++)
                        {
                            uint size = 0u;
                            if (fread(&size, 4, 1, file!) != 1 || fread(&expected[0], 1, (nuint)size, file!) != (nuint)size) return 5;
                            if (!first.MoveNext() || !second.MoveNext()) return 6;
                            Span<readonly(char)> a = first.Current;
                            Span<readonly(char)> b = second.Current;
                            Span<readonly(char)> repeated = first.Current;
                            if (a.Length != (nuint)size || b.Length != (nuint)size || repeated.Data != a.Data) return 7;
                            for (nuint i = 0; i < (nuint)size; i++) { if (a[i] != expected[i] || b[i] != expected[i]) return 8; }
                        }
                        if (first.MoveNext() || second.MoveNext() || first.MoveNext() || !Invalid(&first) || !Invalid(&second)) return 9;
                    }
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedSlicesAreReusableAndNoAllocationsAreNeededForSuccessfulEnumeration(int optimization)
        => Run("""
            using Standard;
            using Standard::Text;
            using Standard::Memory;

            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow { }
            }
            int main()
            {
                char[6] data = ['a', ':', ':', 'b', ':', ':'];
                Span<readonly(char)> text = new(&data[0], 6);
                var parts = text.Split("::");
                data[0] = 'z';
                for (int pass = 0; pass < 2; pass++)
                {
                    var iterator = parts.AsEnumerable().GetEnumerator();
                    if (!iterator.MoveNext() || iterator.Current.Length != 1 || iterator.Current[0] != 'z') return 1;
                    if (!iterator.MoveNext() || iterator.Current.Length != 1 || iterator.Current[0] != 'b') return 2;
                    if (!iterator.MoveNext() || iterator.Current.Length != 0 || iterator.MoveNext()) return 3;
                }
                var empty = text.Split("", true).GetEnumerator();
                if (!empty.MoveNext() || empty.Current.Length != 6 || empty.MoveNext()) return 4;
                Span<readonly(char)> zero = new();
                var noParts = zero.Split("::", true).GetEnumerator();
                if (noParts.MoveNext() || attempts != 0) return 5;
                return 0;
            }
            """, optimization);
}
