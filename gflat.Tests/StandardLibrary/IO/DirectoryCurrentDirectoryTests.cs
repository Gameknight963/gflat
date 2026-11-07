namespace gflat.Tests.Std.IO;

public sealed class DirectoryCurrentDirectoryTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("CURRENT_DIRECTORY.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/IO/IOException.gf", "libc/IO/Directory.gf"))
        .Append(new SourceFile("current-directory.gf", code));

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RelativeChangesAndFailuresMatchHostPathsWithoutChangingTheTestProcess(int optimization)
    {
        string original = Directory.GetCurrentDirectory();
        string root = Path.Combine(Path.GetTempPath(), "gflat_current_directory_" + Guid.NewGuid().ToString("N"));
        string parent = Path.Combine(root, "a directory with enough characters to grow");
        string child = Path.Combine(parent, "inner");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "marker.bin"), "X");
        try
        {
            Run($$"""
                using Standard;
                using Standard::IO;
                using Standard::Text;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" int fgetc(void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    String original = Directory::GetCurrentDirectory();
                    defer { Directory::SetCurrentDirectory(original.Data); }
                    Directory::SetCurrentDirectory({{Literal(child)}});
                    String snapshot = Directory::GetCurrentDirectory();
                    if (!snapshot.Equals({{Literal(child)}})) return 1;
                    void*? file = fopen("marker.bin", "rb");
                    if (file == null) return 2;
                    int first = fgetc(file!);
                    fclose(file!);
                    if (first != 88) return 3;
                    Directory::SetCurrentDirectory("..");
                    String parent = Directory::GetCurrentDirectory();
                    if (!parent.Equals({{Literal(parent)}}) || !snapshot.Equals({{Literal(child)}})) return 4;
                    Directory::SetCurrentDirectory("inner/.");
                    readonly(char)*[3] invalid = ["", "missing", "marker.bin"];
                    for (nuint i = 0; i < 3; i++)
                    {
                        bool caught = false;
                        try { Directory::SetCurrentDirectory(invalid[i]); }
                        catch (IOException* error) { caught = true; }
                        String current = Directory::GetCurrentDirectory();
                        if (!caught || !current.Equals({{Literal(child)}})) return 5;
                    }
                    return 0;
                }
                """, optimization);
            Assert.Equal(original, Directory.GetCurrentDirectory());
            Assert.Equal("X", File.ReadAllText(Path.Combine(child, "marker.bin")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryObservedBufferAllocationFailurePreservesAssignmentAndCurrentDirectory(int optimization)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat_current_directory_" + Guid.NewGuid().ToString("N"));
        string child = Path.Combine(root, "a directory with enough characters to grow");
        Directory.CreateDirectory(child);
        Assert.True(child.Length > 64);
        try
        {
            Run($$"""
                using Standard;
                using Standard::IO;
                using Standard::Text;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                int requests = 0;
                int allocated = 0;
                int released = 0;
                int failAt = 0;
                bool deny = false;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        requests++;
                        if (deny || requests == failAt) return null;
                        void*? result = malloc(bytes);
                        if (result != null) allocated++;
                        return result;
                    }
                    public replace void Free(void* storage) nothrow { released++; free(storage); }
                }
                int main()
                {
                    {
                        String original = Directory::GetCurrentDirectory();
                        defer { Directory::SetCurrentDirectory(original.Data); }
                        deny = true;
                        Directory::SetCurrentDirectory({{Literal(child)}});
                        deny = false;
                        int observed = 0;
                        {
                            requests = 0;
                            String current = Directory::GetCurrentDirectory();
                            observed = requests;
                            if (!current.Equals({{Literal(child)}}) || observed < 2) return 1;
                        }
                        String output = s"previous";
                        readonly(char)* before = output.Data;
                        int baseline = allocated - released;
                        for (int failure = 1; failure <= observed; failure++)
                        {
                            requests = 0;
                            failAt = failure;
                            bool caught = false;
                            try { output = Directory::GetCurrentDirectory(); }
                            catch (BadAllocException* error) { caught = true; }
                            failAt = 0;
                            if (!caught || output.Data != before || !output.Equals("previous")
                                || allocated - released != baseline) return 2;
                            {
                                String current = Directory::GetCurrentDirectory();
                                if (!current.Equals({{Literal(child)}})) return 3;
                            }
                        }
                        if (allocated - released != baseline) return 4;
                    }
                    if (allocated != released) return 5;
                    return 0;
                }
                """, optimization);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root); }
    }
}
