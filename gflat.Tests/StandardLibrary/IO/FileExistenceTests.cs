using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class FileExistenceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task QueriesDistinguishFilesDirectoriesAndFailedLookupsWithoutAllocatingPaths(int optimization)
    {
        string directory = CreateDirectory();
        string file = Path.Combine(directory, "read only file.bin");
        try
        {
            File.WriteAllText(file, "contents");
            File.SetAttributes(file, FileAttributes.ReadOnly);
            UnixFileMode originalMode = default;
            if (!OperatingSystem.IsWindows())
            {
                originalMode = File.GetUnixFileMode(file);
                File.SetUnixFileMode(file, UnixFileMode.None);
            }
            try
            {
                string executable = Compile(directory, """
                    using Standard::IO;

                    static class State { public static int Allocations; }
                    namespace Memory {
                        public replace void*? Allocate(nuint bytes) { State::Allocations++; return null; }
                        public replace void Free(void* memory) nothrow {}
                    }
                    int main(int argc, char** argv) {
                        int[9] expected = [1, 2, 0, 0, 0, 2, 1, 2, 0];
                        for (int index = 0; index < 9; index++) {
                            int kind = 0;
                            if (File::Exists(argv[index + 1])) kind++;
                            if (Directory::Exists(argv[index + 1])) kind += 2;
                            if (kind != expected[index]) return index + 1;
                        }
                        if (State::Allocations != 0) return 10;
                        return 0;
                    }
                    """, optimization);
                string[] arguments = [file, directory, Path.Combine(directory, "missing"), "",
                    Path.Combine(file, "child"), ".", file, directory + Path.DirectorySeparatorChar,
                    file + Path.DirectorySeparatorChar];
                var result = NativeToolchain.Run(executable, arguments, 30000);
                Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            }
            finally
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, originalMode);
            }
        }
        finally
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
            await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FileQueriesObserveRenameAndDeletionImmediately(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    if (!File::Exists(argv[1]) || File::Exists(argv[2])) return 20;
                    File::Rename(argv[1], argv[2]);
                    if (File::Exists(argv[1]) || !File::Exists(argv[2]) || Directory::Exists(argv[2])) return 21;
                    File::Remove(argv[2]);
                    if (File::Exists(argv[1]) || File::Exists(argv[2]) || Directory::Exists(argv[2])) return 22;
                    return 0;
                }
                """, optimization);
            string source = Path.Combine(directory, "source.bin"), destination = Path.Combine(directory, "renamed.bin");
            File.WriteAllText(source, "contents");
            var result = NativeToolchain.Run(executable, [source, destination], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.False(File.Exists(source));
            Assert.False(File.Exists(destination));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task QueriesFollowFileAndDirectoryLinksAndRejectDanglingOrLoopingLinks(int optimization)
    {
        if (OperatingSystem.IsWindows())
        {
            output.WriteLine("The symbolic-link case runs on Unix without additional privileges.");
            return;
        }
        string directory = CreateDirectory();
        try
        {
            string file = Path.Combine(directory, "target.bin"), subdirectory = Path.Combine(directory, "target-directory");
            File.WriteAllText(file, "contents");
            Directory.CreateDirectory(subdirectory);
            string fileLink = Path.Combine(directory, "file-link"), directoryLink = Path.Combine(directory, "directory-link");
            string dangling = Path.Combine(directory, "dangling"), loop = Path.Combine(directory, "loop");
            File.CreateSymbolicLink(fileLink, Path.GetFileName(file));
            Directory.CreateSymbolicLink(directoryLink, Path.GetFileName(subdirectory));
            File.CreateSymbolicLink(dangling, "missing");
            File.CreateSymbolicLink(loop, "loop");
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    if (!File::Exists(argv[1]) || Directory::Exists(argv[1])) return 30;
                    if (File::Exists(argv[2]) || !Directory::Exists(argv[2])) return 31;
                    if (File::Exists(argv[3]) || Directory::Exists(argv[3])) return 32;
                    if (File::Exists(argv[4]) || Directory::Exists(argv[4])) return 33;
                    File::Remove(argv[1]);
                    if (!File::Exists(argv[5]) || File::Exists(argv[1])) return 34;
                    return 0;
                }
                """, optimization);
            var result = NativeToolchain.Run(executable, [fileLink, directoryLink, dangling, loop, file], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DirectoryQueriesWorkWithAnExplicitDirectorySourceClosure(int optimization)
    {
        string[] sources = ["core/IO/IOException.gf", "libc/IO/Directory.gf"];
        var files = sources.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))));
        string ir = Compiler.Emit(files.Append(new SourceFile("directory-only.gf", """
            using Standard::IO;

            int main() nothrow {
                if (!Directory::Exists(".") || Directory::Exists("")) return 1;
                Directory::CreateDirectory(".");
                return 0;
            }
            """)), options: new() { InternalizeFunctions = true });
        Assert.Equal((true, true), NativeToolchain.RequiredNativeRuntimes(ir));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task DirectoryQueriesObserveRenamedAndDeletedDirectories(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    bool expected = argv[2][0] == '1';
                    if (Directory::Exists(argv[1]) != expected || File::Exists(argv[1])) return 40;
                    return 0;
                }
                """, optimization);
            string first = Path.Combine(directory, "first"), second = Path.Combine(directory, "second");
            Directory.CreateDirectory(first);
            CheckProbe(executable, first, true);
            Directory.Move(first, second);
            CheckProbe(executable, first, false);
            CheckProbe(executable, second, true);
            Directory.Delete(second);
            CheckProbe(executable, second, false);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static void CheckProbe(string executable, string path, bool expected)
    {
        var result = NativeToolchain.Run(executable, [path, expected ? "1" : "0"], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-exists-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Compile(string directory, string code, int optimization)
    {
        string[] sources = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf",
            "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf", "libc/IO/Directory.gf"];
        var files = FileApiTestSources.WithConveniences(sources)
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("file-exists.gf", code));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
