using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class PathMultiComponentTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("PATH_MULTI_COMPONENT.md", 0);

    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/PathStyle.gf", "core/IO/Paths.gf", "core/Text/string_span.gf"))
            .Append(new SourceFile("multi-component-paths.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Path components exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitStylesAndCountedComponentsMatchIndependentChainedPathModel(int optimization)
    {
        string[] choices = ["", "a", "b/", "c\\", "/", "\\", "/root", "C:", "C:tail", "D:\\root", "//host/share", "a\0b", "?", "..", " x "];
        var cases = new List<string[]>();
        for (int mask = 0; mask < 16; mask++)
            cases.Add(Enumerable.Range(0, 4).Select(i => (mask & (1 << i)) != 0 ? "" : ((char)('a' + i)).ToString()).ToArray());
        cases.AddRange(new[] { new[] { "/", "/", "a", "b" }, new[] { "a/", "/b/", "/", "/c" },
            new[] { "a", "C:tail", "\\root", "d" }, new[] { "a", "b", "c", "/last" } });
        var random = new Random(139827);
        for (int i = 0; i < 128; i++) cases.Add(Enumerable.Range(0, 4).Select(_ => choices[random.Next(choices.Length)]).ToArray());
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
        foreach (bool windows in new[] { false, true })
        foreach (string[] parts in cases)
        {
            writer.Write((byte)(windows ? 1 : 0));
            foreach (string part in parts) Write(writer, part);
            foreach (bool cstring in new[] { false, true })
            {
                string[] values = cstring ? parts.Select(p => p.Split('\0')[0]).ToArray() : parts;
                Write(writer, Fold(values.Take(3), windows, false));
                Write(writer, Fold(values.Take(3), windows, true));
                Write(writer, Fold(values, windows, false));
                Write(writer, Fold(values, windows, true));
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Tracking + $$"""
            int Nibble(char value) nothrow
            {
                if (value <= '9') return value - '0';
                return value - 'A' + 10;
            }
            nuint ReadByte(readonly(char)* packet, ref nuint at) nothrow
            {
                nuint value = (nuint)(Nibble(packet[at]) * 16 + Nibble(packet[at + 1]));
                at += 2;
                return value;
            }
            String ReadText(readonly(char)* packet, ref nuint at)
            {
                nuint length = ReadByte(packet, ref at);
                length += ReadByte(packet, ref at) * 256;
                String result = new();
                result.Resize(length);
                for (nuint i = 0; i < length; i++) result[i] = (char)ReadByte(packet, ref at);
                return move result;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint at = 0;
                for (int row = 0; row < {{cases.Count * 2}}; row++)
                {
                    PathStyle style = PathStyle::Unix;
                    if (ReadByte(packet, ref at) != 0) style = PathStyle::Windows;
                    String first = ReadText(packet, ref at);
                    String second = ReadText(packet, ref at);
                    String third = ReadText(packet, ref at);
                    String fourth = ReadText(packet, ref at);
                    nuint firstHash = first.GetHashCode();
                    nuint secondHash = second.GetHashCode();
                    nuint thirdHash = third.GetHashCode();
                    nuint fourthHash = fourth.GetHashCode();
                    for (int form = 0; form < 2; form++)
                    for (int operation = 0; operation < 4; operation++)
                    {
                        String expected = ReadText(packet, ref at);
                        String result = new();
                        attempts = 0;
                        if (form == 0)
                        {
                            if (operation == 0) result = Paths::Join(first.AsReadOnly(), second.AsReadOnly(), third.AsReadOnly(), style);
                            else if (operation == 1) result = Paths::Combine(first.AsReadOnly(), second.AsReadOnly(), third.AsReadOnly(), style);
                            else if (operation == 2) result = Paths::Join(first.AsReadOnly(), second.AsReadOnly(), third.AsReadOnly(), fourth.AsReadOnly(), style);
                            else result = Paths::Combine(first.AsReadOnly(), second.AsReadOnly(), third.AsReadOnly(), fourth.AsReadOnly(), style);
                        }
                        else
                        {
                            if (operation == 0) result = Paths::Join(first.Data, second.Data, third.Data, style);
                            else if (operation == 1) result = Paths::Combine(first.Data, second.Data, third.Data, style);
                            else if (operation == 2) result = Paths::Join(first.Data, second.Data, third.Data, fourth.Data, style);
                            else result = Paths::Combine(first.Data, second.Data, third.Data, fourth.Data, style);
                        }
                        int allocations = 1;
                        if (expected.IsEmpty) allocations = 0;
                        if (attempts != allocations || !result.Equals(&expected)) return 1 + operation;
                    }
                    if (first.GetHashCode() != firstHash || second.GetHashCode() != secondHash || third.GetHashCode() != thirdHash || fourth.GetHashCode() != fourthHash) return 5;
                }
                if (at != {{hex.Length}}) return 6;
                return live;
            }
            """, optimization);
    }

    private const string Tracking = """
        using Standard;
        using Standard::IO;
        using Standard::Memory;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int attempts = 0;
        int live = 0;
        bool deny = false;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (deny) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailurePreservesAssignmentAndEmptyInputsNeedNoStorage(int optimization)
        => Run(Tracking + """
            String Call(int mode)
            {
                Span<readonly(char)> a = new("a", 1);
                Span<readonly(char)> b = new("b", 1);
                Span<readonly(char)> c = new("c", 1);
                Span<readonly(char)> d = new("d", 1);
                if (mode == 0) return Paths::Join("a", "b", "c", PathStyle::Unix);
                if (mode == 1) return Paths::Join("a", "b", "c", "d", PathStyle::Unix);
                if (mode == 2) return Paths::Combine("a", "b", "c", PathStyle::Unix);
                if (mode == 3) return Paths::Combine("a", "b", "c", "d", PathStyle::Unix);
                if (mode == 4) return Paths::Join(a, b, c, PathStyle::Unix);
                if (mode == 5) return Paths::Join(a, b, c, d, PathStyle::Unix);
                if (mode == 6) return Paths::Combine(a, b, c, PathStyle::Unix);
                if (mode == 7) return Paths::Combine(a, b, c, d, PathStyle::Unix);
                if (mode == 8) return Paths::Join("a", "b", PathStyle::Unix);
                if (mode == 9) return Paths::Combine("a", "b", PathStyle::Unix);
                if (mode == 10) return Paths::Join(a, b, PathStyle::Unix);
                return Paths::Combine(a, b, PathStyle::Unix);
            }
            int main()
            {
                {
                    String target = s"old";
                    readonly(char)* original = target.Data;
                    int baseline = live;
                    for (int mode = 0; mode < 12; mode++)
                    {
                        deny = true;
                        bool caught = false;
                        try { target = Call(mode); } catch (BadAllocException* error) { caught = true; }
                        deny = false;
                        if (!caught || live != baseline || target.Data != original || !target.Equals("old")) return 1;
                    }
                    Span<readonly(char)> empty = new();
                    attempts = 0; deny = true;
                    String a = Paths::Join(empty, empty, empty, PathStyle::Unix);
                    String b = Paths::Join(empty, empty, empty, empty, PathStyle::Windows);
                    String c = Paths::Combine(empty, empty, empty, PathStyle::Unix);
                    String d = Paths::Combine(empty, empty, empty, empty, PathStyle::Windows);
                    deny = false;
                    if (attempts != 0 || !a.IsEmpty || !b.IsEmpty || !c.IsEmpty || !d.IsEmpty) return 2;
                }
                return live;
            }
            """, optimization);

    private static bool Separator(char value, bool windows) => value == '/' || windows && value == '\\';
    private static bool Rooted(string path, bool windows) => path.Length > 0 && (Separator(path[0], windows)
        || windows && path.Length >= 2 && ((path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z')) && path[1] == ':');
    private static string Fold(IEnumerable<string> parts, bool windows, bool combine)
    {
        string result = "";
        foreach (string part in parts)
        {
            if (combine && Rooted(part, windows)) { result = part; continue; }
            if (result.Length == 0) { result = part; continue; }
            if (part.Length == 0) continue;
            bool left = Separator(result[^1], windows), right = Separator(part[0], windows);
            if (left && right) result += part[1..];
            else result += (left || right ? "" : windows ? "\\" : "/") + part;
        }
        return result;
    }
    private static void Write(BinaryWriter writer, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
}
