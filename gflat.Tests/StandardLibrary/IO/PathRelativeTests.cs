using System.Text;

namespace gflat.Tests.Std.IO;

public sealed partial class PathRelativeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GuideExamplesCompile(int index) => StandardLibraryGuideTests.CheckProgram("PATH_RELATIVE.md", index);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/IO/PathStyle.gf", "core/IO/Paths.gf"))
        .Append(new SourceFile("path-relative.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AbsoluteHostPathsMatchDotNetAndExplicitStylesHandleRootsAndComponentBoundaries(int optimization)
    {
        var cases = new List<(string From, string To, bool Windows, string Expected)>();
        var random = new Random(719283);
        string[] names = ["a", "ab", "beta", "gamma", "file.txt", "b2", "space name"];
        bool windows = OperatingSystem.IsWindows();
        string MakePath()
        {
            string root = windows ? @"C:\" : "/";
            string separator = windows ? "\\" : "/";
            string result = root + string.Join(separator, Enumerable.Range(0, random.Next(6))
                .Select(_ => names[random.Next(names.Length)]));
            if (random.Next(3) == 0 && !Path.EndsInDirectorySeparator(result)) result += separator;
            return result;
        }
        for (int i = 0; i < 512; i++)
        {
            string from = MakePath(), to = MakePath();
            cases.Add((from, to, windows, Path.GetRelativePath(from, to)));
        }
        cases.AddRange(new (string, string, bool, string)[]
        {
            ("/a/b", "/a/c", false, "../c"),
            ("/a", "/ab", false, "../ab"),
            ("/a/", "/a", false, "."),
            ("/a", "/a/b/", false, "b/"),
            ("/a/b", "/a/", false, ".."),
            ("/a/b", "/", false, "../.."),
            ("/", "/a/b", false, "a/b"),
            ("/a/./b/../c", "/a/d//e/", false, "../d/e/"),
            ("/A", "/a", false, "../a"),
            (@"C:\a\b", @"c:/A/c/", true, @"..\c\"),
            (@"C:\a", @"D:\b\..\c", true, @"D:\c"),
            (@"C:\a", @"C:\ab", true, @"..\ab"),
            (@"C:\a\b", @"c:\A\", true, ".."),
            (@"\\Server\Share\a", @"//server/share/b/", true, @"..\b\"),
            (@"\\Server\Share", @"\\server\share\", true, "."),
            (@"\\s\one\a", @"\\s\two\b", true, @"\\s\two\b"),
            (@"C:\a", @"\\s\share\b", true, @"\\s\share\b"),
            ("/\u00E9/\u03B2", "/\u00E9/\u03B3", false, "../\u03B3"),
            (@"C:\" + "\u00E9", @"c:\" + "\u00C9", true, @"..\" + "\u00C9")
        });
        using var packet = new MemoryStream();
        foreach (var row in cases)
        {
            packet.WriteByte(row.Windows ? (byte)1 : (byte)0);
            foreach (string value in new[] { row.From, row.To, row.Expected })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                Assert.InRange(bytes.Length, 1, 255);
                packet.WriteByte((byte)bytes.Length);
                packet.Write(bytes);
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            using Standard;
            using Standard::IO;
            using Standard::Memory;
            using Standard::Text;

            int Digit(char value)
            {
                if (value <= '9') return value - '0';
                return value - 'A' + 10;
            }
            int Read(readonly(char)* packet, ref nuint at)
            {
                int value = Digit(packet[at]) * 16 + Digit(packet[at + 1]);
                at += 2;
                return value;
            }
            String Text(readonly(char)* packet, ref nuint at)
            {
                nuint count = (nuint)Read(packet, ref at);
                String result = new();
                result.Resize(count);
                for (nuint i = 0; i < count; i++) result[i] = (char)Read(packet, ref at);
                return move result;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint at = 0;
                for (int row = 0; row < {{cases.Count}}; row++)
                {
                    PathStyle style = PathStyle::Unix;
                    if (Read(packet, ref at) == 1) style = PathStyle::Windows;
                    String origin = Text(packet, ref at);
                    String target = Text(packet, ref at);
                    String expected = Text(packet, ref at);
                    String result = Paths::GetRelativePath(origin.Data, target.Data, style);
                    if (!result.Equals(&expected)) return 1;
                    Span<readonly(char)> first = new(origin.Data, origin.Length);
                    Span<readonly(char)> second = new(target.Data, target.Length);
                    String counted = Paths::GetRelativePath(first, second, style);
                    if (!counted.Equals(&expected)) return 2;
                    String roundTrip = Paths::GetFullPath(result.Data, origin.Data, style);
                    String normalized = Paths::GetFullPath(target.Data, target.Data, style);
                    if (style == PathStyle::Unix && !roundTrip.Equals(&normalized))
                    {
                        // Relative paths to an ancestor omit a redundant trailing separator.
                        String a = Paths::TrimEndingDirectorySeparator(roundTrip.Data, style);
                        String b = Paths::TrimEndingDirectorySeparator(normalized.Data, style);
                        if (!a.Equals(&b)) return 3;
                    }
                }
                if (at != {{hex.Length}}) return 4;
                return 0;
            }
            """, optimization);
    }
}
