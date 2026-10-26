using System.Text;

namespace gflat.Tests;

public sealed class StandardPathEdgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RootEdgesAndDotComponentsFollowExplicitPortableLexicalRules(int optimization)
    {
        (string Style, string Path, string Name, string Directory, string Extension, string Stem, string Root)[] cases =
        [
            ("Windows", "C:.", ".", "C:", "", "", "C:"),
            ("Windows", "C:..", "..", "C:", "", ".", "C:"),
            ("Windows", "C:folder//", "", "C:folder", "", "", "C:"),
            ("Windows", "//", "", "", "", "", "//"),
            ("Windows", "//host", "", "", "", "", "//host"),
            ("Windows", "//host/", "", "", "", "", "//host/"),
            ("Windows", "//host/share", "", "", "", "", "//host/share"),
            ("Windows", "//host/share/", "", "", "", "", "//host/share/"),
            ("Windows", "//host/share//leaf", "leaf", "//host/share/", "", "leaf", "//host/share/"),
            ("Windows", "//host/share/../file.tar", "file.tar", "//host/share/..", ".tar", "file", "//host/share/"),
            ("Windows", "//host/share/.", ".", "//host/share/", "", "", "//host/share/"),
            ("Windows", @"\/host\share/sub//file.", "file.", @"\/host\share/sub", "", "file", @"\/host\share/"),
            ("Unix", "//", "", "/", "", "", "/"),
            ("Unix", "//host/share", "share", "//host", "", "share", "/"),
            ("Unix", "///leaf", "leaf", "/", "", "leaf", "/"),
            ("Unix", @"dir\leaf.ext", @"dir\leaf.ext", "", ".ext", @"dir\leaf", ""),
            ("Unix", ".//..", "..", ".", "", ".", ""),
            ("Unix", "dir/../.", ".", "dir/..", "", "", ""),
            ("Unix", "C:folder/", "", "C:folder", "", "", ""),
            ("Unix", "...", "...", "", "", "..", "")
        ];
        var code = new StringBuilder("using std; using std::Memory; using std::Queries; using std::Collections; int main() {\n");
        foreach (var item in cases)
        {
            string[] methods = ["GetFileName", "GetDirectoryName", "GetExtension", "GetFileNameWithoutExtension", "GetRoot"];
            string[] expected = [item.Name, item.Directory, item.Extension, item.Stem, item.Root];
            for (int i = 0; i < methods.Length; i++)
                code.AppendLine($$"""
                    { String result = Paths::{{methods[i]}}({{Literal(item.Path)}}, PathStyle::{{item.Style}});
                      if (result.Length != {{expected[i].Length}} || !result.StartsWith({{Literal(expected[i])}}) || result.Data[result.Length] != '\0') return {{i + 1}}; }
                    """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnedResultsPreserveSpellingAndDoNotAliasInputOrEachOther(int optimization)
        => Run("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                char[10] input = ['a', '/', 'b', '.', 'c', '\0', 'x', '.', 'y', '\0'];
                String name = Paths::GetFileName(input, PathStyle::Unix);
                String directory = Paths::GetDirectoryName(input, PathStyle::Unix);
                String extension = Paths::GetExtension(input, PathStyle::Unix);
                String stem = Paths::GetFileNameWithoutExtension(input, PathStyle::Unix);
                String joined = Paths::Join(input, "../z", PathStyle::Unix);
                String copied = Paths::Join("", input, PathStyle::Unix);
                input[0] = 'X'; input[2] = 'Y'; input[4] = 'Z';
                if (name.Length != 3 || !name.StartsWith("b.c")) return 1;
                if (directory.Length != 1 || directory.Data[0] != 'a') return 2;
                if (extension.Length != 2 || !extension.StartsWith(".c") || stem.Length != 1 || stem.Data[0] != 'b') return 3;
                if (joined.Length != 10 || !joined.StartsWith("a/b.c/../z")) return 4;
                if (copied.Length != 5 || !copied.StartsWith("a/b.c")) return 5;
                name.Replace('b', 'q');
                if (stem.Data[0] != 'b' || joined.Data[2] != 'b' || copied.Data[2] != 'b') return 6;
                String driveJoin = Paths::Join("C:", "child", PathStyle::Windows);
                String rootedRight = Paths::Join("left", "C:/child", PathStyle::Windows);
                String doubled = Paths::Join("a//", "//b", PathStyle::Unix);
                if (!driveJoin.StartsWith("C:\\child") || !rootedRight.StartsWith("left\\C:/child") || !doubled.StartsWith("a///b")) return 7;
                return 0;
            }
            """, optimization);

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string code, int optimization)
    {
        var files = new[] { "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Memory/Span.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf", "core/IO/PathStyle.gf", "core/IO/Paths.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", code));
        var result = CompilerTestHelper.Run(files, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
