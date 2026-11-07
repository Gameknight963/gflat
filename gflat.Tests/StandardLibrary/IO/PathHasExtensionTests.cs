using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class PathHasExtensionTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("PATH_EXTENSION_TEST.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StylesAndCountedInputsFollowExtensionRulesWithoutAllocation(int optimization)
    {
        string[] names = ["", ".", "..", "file", "file.", "file.tar.", "file.tar.gz", ".config", "..config", "file..x", "file. "];
        string[] prefixes = ["", "dir/", "dir.ext/", "/", "dir.ext\\"];
        var checks = new StringBuilder();
        foreach (bool windows in new[] { false, true })
        foreach (string prefix in prefixes)
        foreach (string name in names)
        {
            string path = prefix + name;
            int separator = path.LastIndexOf('/');
            if (windows) separator = Math.Max(separator, path.LastIndexOf('\\'));
            int dot = path.LastIndexOf('.');
            bool expected = dot > separator && dot != path.Length - 1;
            if (windows == OperatingSystem.IsWindows()) Assert.Equal(expected, Path.HasExtension(path));
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            checks.AppendLine($"if (!Check({literal}, PathStyle::{(windows ? "Windows" : "Unix")}, {(expected ? "true" : "false")})) return 1;");
        }
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/Paths.gf", "core/IO/PathStyle.gf"))
            .Append(new SourceFile("path-has-extension.gf", $$"""
            using Standard;
            using Standard::IO;
            using Standard::Memory;

            extern "C" void free(void* storage);
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            bool Check(readonly(char)* path, PathStyle style, bool expected) nothrow
            {
                nuint count = 0;
                while (path[count] != '\0') count++;
                if (Paths::HasExtension(path, style) != expected) return false;
                Span<readonly(char)> view = new(path, count);
                if (Paths::HasExtension(view, style) != expected) return false;
                return true;
            }
            int main()
            {
                {{checks}}
                if (Paths::HasExtension(new Span<readonly(char)>(), PathStyle::Windows)) return 2;
                char[5] counted = ['a', '.', '\0', 'x', '/'];
                if (Paths::HasExtension(&counted[0], PathStyle::Unix)) return 3;
                if (!Paths::HasExtension(new Span<readonly(char)>(&counted[0], 4), PathStyle::Unix)) return 4;
                if (Paths::HasExtension(new Span<readonly(char)>(&counted[0], 5), PathStyle::Unix)) return 5;
                if (attempts != 0) return 6;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"HasExtension exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
