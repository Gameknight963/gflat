using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class PathFullPathTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AbsoluteAndRelativeInputsNormalizeAgainstExplicitRootsOnEveryHost(int optimization)
    {
        (string Path, string Base, string Style, string Expected)[] cases =
        [
            ("", "/a/b/", "Unix", "/a/b/"),
            (".", "/a/b/", "Unix", "/a/b"),
            ("./", "/a/b", "Unix", "/a/b/"),
            ("../c/./d//", "/a/b/", "Unix", "/a/c/d/"),
            ("../../../c", "/a/b", "Unix", "/c"),
            ("../../..", "/a/b", "Unix", "/"),
            ("/x//./y/../z", "/unused", "Unix", "/x/z"),
            ("//x///y/", "/unused", "Unix", "/x/y/"),
            (".../file", "/a/./b/../c", "Unix", "/a/c/.../file"),
            (@"C:\x\..\y", "/base", "Unix", @"/base/C:\x\..\y"),
            ("", @"c:/a/./b/../", "Windows", @"c:\a\"),
            (@"..\c/./d\\", @"C:\a\b\", "Windows", @"C:\a\c\d\"),
            (@"..\..\..\c", @"C:\a\b", "Windows", @"C:\c"),
            (@"..\..\..", @"C:\a\b", "Windows", @"C:\"),
            (@"\rooted\..\x", @"C:\a\b", "Windows", @"C:\x"),
            (@"D:/x/../y/", @"C:\a\b", "Windows", @"D:\y\"),
            (@"..\x", @"\\server\share\a\b", "Windows", @"\\server\share\a\x"),
            (@"..\..\..\x", @"\\server\share\a", "Windows", @"\\server\share\x"),
            (@"\x", @"\\server\share\a\b", "Windows", @"\\server\share\x"),
            (@"\\other\share\x\.\", @"C:\unused", "Windows", @"\\other\share\x\"),
            (@"//server/share", @"C:\unused", "Windows", @"\\server\share"),
            (@"//server/share/", @"C:\unused", "Windows", @"\\server\share\"),
            (@"a\..\...\file", @"C:\base", "Windows", @"C:\base\...\file"),
            ("../β/./尾/", "/资料/α", "Unix", "/资料/β/尾/"),
            (@"..\β\尾", @"\\sérver\共享\α", "Windows", @"\\sérver\共享\β\尾")
        ];
        var code = new StringBuilder(Imports + "int main() {\n");
        foreach (var item in cases)
        {
            code.AppendLine($$"""
                {
                    String value = Paths::GetFullPath({{Literal(item.Path)}}, {{Literal(item.Base)}}, PathStyle::{{item.Style}});
                    if (!value.Equals({{Literal(item.Expected)}})) return 1;
                    String input = String::From({{Literal(item.Path)}});
                    String basePath = String::From({{Literal(item.Base)}});
                    Span<readonly(char)> inputView = new(input.Data, input.Length);
                    Span<readonly(char)> baseView = new(basePath.Data, basePath.Length);
                    String counted = Paths::GetFullPath(inputView, baseView, PathStyle::{{item.Style}});
                    if (!counted.Equals({{Literal(item.Expected)}})) return 2;
                }
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedPathsPreserveEmbeddedZerosAndBorrowedSourceBytes(int optimization)
        => Run(Imports + """
            int main() {
                char[5] baseBytes = ['/', 'x', '\0', 'y', '!'];
                char[7] pathBytes = ['.', '.', '/', 'z', '\0', 'q', '!'];
                char[5] originalBase = baseBytes;
                char[7] originalPath = pathBytes;
                Span<readonly(char)> basePath = new(baseBytes, 4);
                Span<readonly(char)> path = new(pathBytes, 6);
                String result = Paths::GetFullPath(path, basePath, PathStyle::Unix);
                if (result.Length != 4 || result.Data[0] != '/' || result.Data[1] != 'z' || result.Data[2] != '\0' || result.Data[3] != 'q' || result.Data[4] != '\0') return 3;
                for (int index = 0; index < 5; index++) if (baseBytes[index] != originalBase[index]) return 4;
                for (int index = 0; index < 7; index++) if (pathBytes[index] != originalPath[index]) return 5;
                Span<readonly(char)> empty = new(((nuint)1).reinterpret<readonly(char)*>(), 0);
                String normalizedBase = Paths::GetFullPath(empty, basePath, PathStyle::Unix);
                if (normalizedBase.Length != 4 || normalizedBase.Data[2] != '\0') return 6;
                result[1] = 'a';
                if (pathBytes[3] != 'z') return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IncompleteBasesDriveRelativeAndDevicePathsAreRejectedWithoutChangingPrefixPredicates(int optimization)
    {
        (string Path, string Base, string Style, string Message)[] invalid =
        [
            ("a", "", "Unix", "fully qualified"),
            ("a", "relative", "Unix", "fully qualified"),
            (@"C:\a", "relative", "Windows", "fully qualified"),
            ("a", @"\base", "Windows", "fully qualified"),
            ("a", @"C:base", "Windows", "fully qualified"),
            (@"C:child", @"C:\base", "Windows", "Drive-relative"),
            (@"D:", @"C:\base", "Windows", "Drive-relative"),
            (@"\\server", @"C:\base", "Windows", "server and share"),
            ("a", @"\\server\", "Windows", "server and share"),
            (@"\\?\C:\child", @"C:\base", "Windows", "Device paths"),
            (@"\\.\pipe\name", @"C:\base", "Windows", "Device paths"),
            ("a", @"\\?\C:\base", "Windows", "Device paths")
        ];
        var code = new StringBuilder(Imports + "int main() {\n");
        foreach (var item in invalid)
        {
            code.AppendLine($$"""
                try { String value = Paths::GetFullPath({{Literal(item.Path)}}, {{Literal(item.Base)}}, PathStyle::{{item.Style}}); return 10; }
                catch (Exception* error) {
                    String message = String::From(error.GetMessage());
                    if (!message.Contains({{Literal(item.Message)}})) return 11;
                }
                """);
        }
        code.AppendLine("""
            try { String value = Paths::GetFullPath("a", "/base", (PathStyle)99); return 12; }
            catch (Exception* error) {}
            if (!Paths::IsFullyQualified("\\\\server", PathStyle::Windows)) return 13;
            if (!Paths::IsRooted("C:child", PathStyle::Windows)) return 14;
            return 0;
            }
            """);
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailureReleasesPartialNormalizationAndPreservesAssignmentTarget(int optimization)
        => Run(Imports + """
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            static class State { public static bool Failing; public static int Remaining; public static int Live; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    if (State::Failing) {
                        if (State::Remaining == 0) return null;
                        State::Remaining--;
                    }
                    void*? storage = malloc(bytes);
                    if (storage != null) State::Live++;
                    return storage;
                }
                public replace void Free(void* storage) nothrow { State::Live--; free(storage); }
            }
            int main() {
                {
                    String destination = String::From("kept");
                    readonly(char)* address = destination.Data;
                    int live = State::Live;
                    State::Failing = true;
                    State::Remaining = 1;
                    try { destination = Paths::GetFullPath("/long-component/next", "/base", PathStyle::Unix); return 20; }
                    catch (BadAllocException* error) {}
                    State::Failing = false;
                    if (State::Live != live || destination.Data != address || !destination.Equals("kept")) return 21;
                }
                if (State::Live != 0) return 22;
                return 0;
            }
            """, optimization);

    private const string Imports = """
        using Standard;
        using Standard::IO;
        using Standard::Memory;
        using Standard::Text;

        """ + "\n";

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf",
            "core/Memory/Span.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf",
            "core/IO/PathStyle.gf", "core/IO/Paths.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("path-full-path.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
