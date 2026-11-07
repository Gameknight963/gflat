namespace gflat.Tests.Std.IO;

public sealed class PathCombineTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/IO/PathStyle.gf", "core/IO/Paths.gf", "core/Text/string_span.gf"))
        .Append(new SourceFile("path-combine.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("PATH_COMBINE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitStylesAndCountedNulsPreserveLexicalSpelling(int optimization)
    {
        var rows = new List<(string Left, string Right, string Expected, bool Windows)>();
        string[] parts = ["", "a", ".", "..", "a/", "a\\", "/", "\\", "/root", "C:", "C:tail", "D:\\root", "\\\\host\\share", "//host/share", "a\0b", " x "];
        foreach (string left in parts)
        foreach (string right in parts) rows.Add((left, right, Path.Combine(left, right), OperatingSystem.IsWindows()));
        rows.AddRange(new[] {
            ("a", "/b", "/b", false), ("a\\b", "c", "a\\b/c", false),
            ("C:\\a", "D:b", "D:b", true), ("C:\\a", "\\b", "\\b", true),
            ("C:\\a", "/b", "/b", true), ("C:", "b", "C:\\b", true),
            ("a\\", "b", "a\\b", true), ("a/", "b", "a/b", true),
            ("left", "\\\\server\\share\\x", "\\\\server\\share\\x", true),
            ("left", "//server/share/x", "//server/share/x", true), ("left", "C:", "C:", true),
            ("left", "1:tail", "left\\1:tail", true), ("x/", "../y", "x/../y", false),
            ("x", "a\0b", "x/a\0b", false), ("x", "C:\0tail", "C:\0tail", true),
            ("a\0b", "c", "a\0b\\c", true)
        });
        var packet = new List<byte>();
        foreach (var row in rows)
        {
            packet.Add((byte)(row.Windows ? 1 : 0));
            packet.Add((byte)row.Left.Length);
            packet.Add((byte)row.Right.Length);
            packet.Add((byte)row.Expected.Length);
            packet.AddRange(row.Left.Select(value => (byte)value));
            packet.AddRange(row.Right.Select(value => (byte)value));
            packet.AddRange(row.Expected.Select(value => (byte)value));
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            using Standard;
            using Standard::IO;
            using Standard::Memory;
            using Standard::Text;

            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            nuint Read(readonly(char)* input, ref nuint position)
            {
                int value = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                position += 2;
                return (nuint)value;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                for (int row = 0; row < {{rows.Count}}; row++)
                {
                    PathStyle style = PathStyle::Unix;
                    if (Read(packet, ref position) != 0) style = PathStyle::Windows;
                    nuint leftLength = Read(packet, ref position);
                    nuint rightLength = Read(packet, ref position);
                    nuint expectedLength = Read(packet, ref position);
                    char[128] left = default(char[128]);
                    char[128] right = default(char[128]);
                    char[256] expected = default(char[256]);
                    bool terminated = true;
                    for (nuint i = 0; i < leftLength; i++)
                    {
                        left[i] = (char)Read(packet, ref position);
                        if (left[i] == '\0') terminated = false;
                    }
                    for (nuint i = 0; i < rightLength; i++)
                    {
                        right[i] = (char)Read(packet, ref position);
                        if (right[i] == '\0') terminated = false;
                    }
                    for (nuint i = 0; i < expectedLength; i++) expected[i] = (char)Read(packet, ref position);
                    Span<readonly(char)> first = new(&left[0], leftLength);
                    Span<readonly(char)> second = new(&right[0], rightLength);
                    String combined = Paths::Combine(first, second, style);
                    if (!combined.Equals(&expected[0], expectedLength) || combined.Data[combined.Length] != '\0') return 1;
                    if (combined.Length != 0 && (combined.Data == &left[0] || combined.Data == &right[0])) return 2;
                    if (terminated)
                    {
                        String other = Paths::Combine(&left[0], &right[0], style);
                        if (!other.Equals(&combined)) return 3;
                    }
                }
                if (position != {{hex.Length}}) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryAllocationFailurePreservesAliasedAssignmentAndRootedResultsOwnTheirBytes(int optimization)
        => Run("""
            using Standard;
            using Standard::IO;
            using Standard::Memory;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int live = 0;
            int remaining = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (remaining == 0) return null;
                    if (remaining > 0) remaining--;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int main()
            {
                {
                    String output = s"left";
                    String right = s"a-long-relative-component";
                    int before = attempts;
                    {
                        String result = Paths::Combine(output.AsReadOnly(), right.AsReadOnly(), PathStyle::Unix);
                        if (!result.Equals("left/a-long-relative-component")) return 1;
                    }
                    int allocations = attempts - before;
                    if (allocations < 1) return 2;
                    for (int failure = 0; failure < allocations; failure++)
                    {
                        readonly(char)* original = output.Data;
                        int originalLive = live;
                        remaining = failure;
                        bool caught = false;
                        try { output = Paths::Combine(output.AsReadOnly(), right.AsReadOnly(), PathStyle::Unix); }
                        catch (Exception* error) { caught = true; }
                        remaining = -1;
                        if (!caught || live != originalLive || output.Data != original || !output.Equals("left")) return 3;
                    }
                    String rooted = s"/rooted";
                    String copy = Paths::Combine(output.AsReadOnly(), rooted.AsReadOnly(), PathStyle::Unix);
                    if (!copy.Equals("/rooted") || copy.Data == rooted.Data) return 4;
                    copy[1] = 'R';
                    if (!rooted.Equals("/rooted")) return 5;
                    int originalLive = live;
                    readonly(char)* original = output.Data;
                    remaining = 0;
                    bool caught = false;
                    try { output = Paths::Combine("ignored", rooted.Data, PathStyle::Unix); }
                    catch (Exception* error) { caught = true; }
                    remaining = -1;
                    if (!caught || output.Data != original || live != originalLive) return 9;
                    caught = false;
                    try { output = Paths::Combine("left", "/right", (PathStyle)2); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || !output.Equals("left")) return 6;
                }
                if (live != 0) return 7;
                remaining = 0;
                int before = attempts;
                String empty = Paths::Combine("", "", PathStyle::Unix);
                Span<readonly(char)> nowhere = new();
                String other = Paths::Combine(nowhere, nowhere, PathStyle::Windows);
                if (!empty.IsEmpty || !other.IsEmpty || attempts != before) return 8;
                remaining = -1;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Path combine exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
