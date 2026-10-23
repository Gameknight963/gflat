namespace gflat.Tests;

public sealed class StandardPathTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string program, int optimization = 0)
    {
        var files = new[] { "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Formatting.gf", "core/Span.gf", "core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf", "core/Paths/PathStyle.gf", "core/Paths/Paths.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        var result = CompilerTestHelper.Run(files, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Equal = """
        using std;
        bool Equal(ref immutable(String) value, immutable(char*) expected) {
            nuint i = 0;
            while (expected[i] != '\0') {
                if (i >= value.Length || value.Data[i] != expected[i]) return false;
                i++;
            }
            return i == value.Length;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RootClassificationDistinguishesWindowsDriveRelativePathsWithoutAllocating(int optimization)
    {
        string[] paths = ["", "a", "C", ":", "1:", "C:", "c:child", "Z:..", "C:/", @"C:\child", "/", @"\", "/child", @"\child", "//", @"\\", @"\\server", @"\\server\share", @"\\?\C:\file", "a/b", "a\\b", "../a", "./", "C::", "C:/../x"];
        var program = new System.Text.StringBuilder("using std; namespace Memory { replace void*? Allocate(nuint size) => null; replace void Free(void* address) nothrow {} } int main(){");
        foreach (string path in paths)
        {
            bool drive = path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
            bool Separator(char value) => value is '/' or '\\';
            bool windowsRooted = drive || (path.Length != 0 && Separator(path[0]));
            bool windowsFully = (path.Length >= 2 && Separator(path[0]) && Separator(path[1])) ||
                (drive && path.Length >= 3 && Separator(path[2]));
            bool unix = path.StartsWith('/');
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(Path.IsPathRooted(path), windowsRooted);
                Assert.Equal(Path.IsPathFullyQualified(path), windowsFully);
            }
            program.AppendLine($$"""
                if(Paths::IsRooted({{Literal(path)}},PathStyle::Unix)!={{unix.ToString().ToLowerInvariant()}})return 1;
                if(Paths::IsFullyQualified({{Literal(path)}},PathStyle::Unix)!={{unix.ToString().ToLowerInvariant()}})return 2;
                if(Paths::IsRooted({{Literal(path)}},PathStyle::Windows)!={{windowsRooted.ToString().ToLowerInvariant()}})return 3;
                if(Paths::IsFullyQualified({{Literal(path)}},PathStyle::Windows)!={{windowsFully.ToString().ToLowerInvariant()}})return 4;
                """);
        }
        program.Append("return 0;}");
        Run(program.ToString(), optimization);
    }

    [Fact]
    public void UnixPathsRemainPurelyLexicalAndIgnoreWindowsSeparators()
    {
        (string Path, string Name, string Directory, string Extension, string Stem, string Root)[] cases =
        [
            ("", "", "", "", "", ""),
            ("/", "", "", "", "", "/"),
            ("/file.txt", "file.txt", "/", ".txt", "file", "/"),
            ("/dir/file.tar.gz", "file.tar.gz", "/dir", ".gz", "file.tar", "/"),
            ("file", "file", "", "", "file", ""),
            ("dir/", "", "dir", "", "", ""),
            ("/dir/", "", "/dir", "", "", "/"),
            ("a//b", "b", "a", "", "b", ""),
            (".config", ".config", "", ".config", "", ""),
            ("file.", "file.", "", "", "file", ""),
            ("../a", "a", "..", "", "a", ""),
            (@"C:\dir\file.txt", @"C:\dir\file.txt", "", ".txt", @"C:\dir\file", "")
        ];
        CheckParts(cases, "Unix");
    }

    [Fact]
    public void WindowsPathsRecognizeDrivesMixedSeparatorsAndUncRoots()
    {
        (string Path, string Name, string Directory, string Extension, string Stem, string Root)[] cases =
        [
            ("", "", "", "", "", ""),
            (@"C:", "", "", "", "", @"C:"),
            (@"C:\", "", "", "", "", @"C:\"),
            (@"C:foo.txt", "foo.txt", "C:", ".txt", "foo", "C:"),
            (@"C:\foo.txt", "foo.txt", @"C:\", ".txt", "foo", @"C:\"),
            (@"C:\dir/file.txt", "file.txt", @"C:\dir", ".txt", "file", @"C:\"),
            (@"\rooted", "rooted", @"\", "", "rooted", @"\"),
            (@"dir\", "", "dir", "", "", ""),
            (@"\\server\share", "", "", "", "", @"\\server\share"),
            (@"\\server\share\", "", "", "", "", @"\\server\share\"),
            (@"\\server\share\file.txt", "file.txt", @"\\server\share\", ".txt", "file", @"\\server\share\"),
            (@"\\server\share\dir\file", "file", @"\\server\share\dir", "", "file", @"\\server\share\"),
            ("/dir/file", "file", "/dir", "", "file", "/")
        ];
        CheckParts(cases, "Windows");
    }

    private static void CheckParts((string Path, string Name, string Directory, string Extension, string Stem, string Root)[] cases, string style)
    {
        string calls = string.Join(Environment.NewLine, cases.Select((item, i) =>
            $"if (!Check({Literal(item.Path)}, {Literal(item.Name)}, {Literal(item.Directory)}, {Literal(item.Extension)}, {Literal(item.Stem)}, {Literal(item.Root)})) return {i + 1};"));
        Run(Equal + $$"""

            bool Check(immutable(char*) path, immutable(char*) name, immutable(char*) directory, immutable(char*) extension, immutable(char*) stem, immutable(char*) root) {
                String a = Paths::GetFileName(path, PathStyle::{{style}});
                String b = Paths::GetDirectoryName(path, PathStyle::{{style}});
                String c = Paths::GetExtension(path, PathStyle::{{style}});
                String d = Paths::GetFileNameWithoutExtension(path, PathStyle::{{style}});
                String e = Paths::GetRoot(path, PathStyle::{{style}});
                return Equal(ref a, name) && Equal(ref b, directory) && Equal(ref c, extension) && Equal(ref d, stem) && Equal(ref e, root);
            }
            int main() {
                {{calls}}
                return 0;
            }
            """);
    }

    [Fact]
    public void JoinOwnsItsResultWithoutResolvingRootsOrDotSegments()
    {
        Run(Equal + """

            int main() {
                String a = Paths::Join("a", "b", PathStyle::Unix);
                String b = Paths::Join("a/", "/b", PathStyle::Unix);
                String c = Paths::Join("a", "/b", PathStyle::Unix);
                String d = Paths::Join("", "a", PathStyle::Unix);
                String e = Paths::Join("a", "", PathStyle::Unix);
                String f = Paths::Join("C:\\a", "..\\b", PathStyle::Windows);
                String g = Paths::Join("C:/a/", "\\b", PathStyle::Windows);
                if (!Equal(ref a, "a/b") || !Equal(ref b, "a/b") || !Equal(ref c, "a/b")) return 1;
                if (!Equal(ref d, "a") || !Equal(ref e, "a")) return 2;
                if (!Equal(ref f, "C:\\a\\..\\b") || !Equal(ref g, "C:/a/b")) return 3;
                return 0;
            }
            """);
    }
}
