using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class DirectoryRemoveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RemovesOnlyTheNamedEmptyDirectoryAndPreservesFailedDestinations(int optimization)
    {
        string directory = CreateDirectory();
        string regular = Path.Combine(directory, "regular.bin");
        try
        {
            string empty = Path.Combine(directory, "empty"), sibling = Path.Combine(directory, "sibling");
            string nonempty = Path.Combine(directory, "nonempty"), missing = Path.Combine(directory, "missing");
            Directory.CreateDirectory(empty);
            Directory.CreateDirectory(sibling);
            Directory.CreateDirectory(nonempty);
            byte[] contents = [0, 255, 65];
            File.WriteAllBytes(Path.Combine(nonempty, "contents.bin"), contents);
            File.WriteAllBytes(regular, contents);
            File.SetAttributes(regular, FileAttributes.ReadOnly);
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    Directory::Remove(argv[1]);
                    if (Directory::Exists(argv[1])) return 1;
                    for (int index = 1; index <= 4; index++) {
                        try { Directory::Remove(argv[index]); return 1 + index; } catch (IOException* error) {}
                    }
                    try { Directory::Remove(""); return 6; } catch (IOException* error) {}
                    return 0;
                }
                """, optimization);
            Check(executable, [empty, nonempty, regular, missing]);
            Assert.False(Directory.Exists(empty));
            Assert.True(Directory.Exists(sibling));
            Assert.Equal(contents, File.ReadAllBytes(Path.Combine(nonempty, "contents.bin")));
            Assert.Equal(contents, File.ReadAllBytes(regular));
        }
        finally
        {
            if (File.Exists(regular)) File.SetAttributes(regular, FileAttributes.Normal);
            await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PosixRejectsFinalDirectoryAndDanglingLinksWithoutTouchingTargets(int optimization)
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("The symbolic-link case runs on Unix without additional privileges."); return; }
        string directory = CreateDirectory();
        try
        {
            string target = Path.Combine(directory, "empty-target"), alias = Path.Combine(directory, "alias"), dangling = Path.Combine(directory, "dangling");
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(alias, "empty-target");
            Directory.CreateSymbolicLink(dangling, "missing-target");
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    for (int index = 1; index < argc; index++) {
                        try { Directory::Remove(argv[index]); return index; } catch (IOException* error) {}
                    }
                    return 0;
                }
                """, optimization);
            Check(executable, [alias, dangling]);
            Assert.True(Directory.Exists(target));
            Assert.Equal("empty-target", new DirectoryInfo(alias).LinkTarget);
            Assert.Equal("missing-target", new DirectoryInfo(dangling).LinkTarget);
            Assert.False(Directory.Exists(Path.Combine(directory, "missing-target")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-directory-remove-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Check(string executable, string[] arguments)
    {
        var result = NativeToolchain.Run(executable, arguments, 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static string Compile(string directory, string code, int optimization)
    {
        string[] sources = ["core/IO/IOException.gf", "libc/IO/Directory.gf"];
        var files = sources.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("directory-remove.gf", code));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
