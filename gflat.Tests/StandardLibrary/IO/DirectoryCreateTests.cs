using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class DirectoryCreateTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CreatesOneDirectoryAndAcceptsExistingRootsRelativeDotsAndHostSeparators(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    for (int index = 1; index < argc; index++) {
                        Directory::CreateDirectory(argv[index]);
                        Directory::CreateDirectory(argv[index]);
                        if (!Directory::Exists(argv[index])) return index;
                    }
                    return 0;
                }
                """, optimization);
            string absolute = Path.Combine(directory, "absolute");
            string trailing = Path.Combine(directory, "trailing") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(Path.Combine(directory, "relative"));
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, directory) + "/relative/./../leaf/";
            string mixed = Path.Combine(directory, "mixed");
            if (OperatingSystem.IsWindows()) mixed = directory.Replace('\\', '/') + "\\mixed/";
            Check(executable, [absolute, trailing, relative, mixed, Path.GetPathRoot(directory)!]);
            Assert.True(Directory.Exists(absolute));
            Assert.True(Directory.Exists(trailing));
            Assert.True(Directory.Exists(Path.Combine(directory, "relative")));
            Assert.True(Directory.Exists(Path.Combine(directory, "leaf")));
            Assert.True(Directory.Exists(mixed));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CreationNeedsNoAllocationAndFailuresPreserveFilesAndMissingParents(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string file = Path.Combine(directory, "blocking.bin"), created = Path.Combine(directory, "no-allocation");
            byte[] original = [0, 255, 65];
            File.WriteAllBytes(file, original);
            string blockedChild = Path.Combine(file, "child");
            string missingParent = Path.Combine(directory, "missing-parent", "child");
            string executable = Compile(directory, """
                using Standard;
                using Standard::IO;

                extern void*? malloc(nuint bytes);
                extern void free(void* data);
                static class State { public static bool Failing; public static int Live; public static int Calls; }
                namespace Memory {
                    public replace void*? Allocate(nuint bytes) {
                        State::Calls++;
                        if (State::Failing) return null;
                        void*? result = malloc(bytes);
                        if (result != null) State::Live++;
                        return result;
                    }
                    public replace void Free(void* data) nothrow { State::Live--; free(data); }
                }
                int main(int argc, char** argv) {
                    State::Failing = true;
                    Directory::CreateDirectory(argv[1]);
                    if (State::Calls != 0) return 1;
                    Directory::CreateDirectory(argv[2]);
                    if (State::Calls != 0 || !Directory::Exists(argv[2])) return 2;
                    State::Failing = false;
                    if (State::Live != 0) return 3;
                    try { Directory::CreateDirectory(""); return 4; } catch (IOException* error) {}
                    try { Directory::CreateDirectory(argv[3]); return 5; } catch (IOException* error) {}
                    if (State::Live != 0) return 6;
                    try { Directory::CreateDirectory(argv[4]); return 7; } catch (IOException* error) {}
                    if (State::Live != 0) return 8;
                    try { Directory::CreateDirectory(argv[5]); return 9; } catch (IOException* error) {}
                    if (State::Live != 0) return 10;
                    return 0;
                }
                """, optimization);
            Check(executable, [directory, created, file, blockedChild, missingParent]);
            Assert.Equal(original, File.ReadAllBytes(file));
            Assert.True(Directory.Exists(created));
            Assert.False(Directory.Exists(Path.Combine(directory, "missing-parent")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WindowsDriveFormsStayUnderTheTestDirectory(int optimization)
    {
        if (!OperatingSystem.IsWindows()) { output.WriteLine("The drive-path case uses Windows path forms."); return; }
        string directory = CreateDirectory();
        try
        {
            string root = Path.GetPathRoot(directory)!;
            if (!string.Equals(root, Path.GetPathRoot(Environment.CurrentDirectory), StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine("The temp directory is on another drive, whose relative current directory is unknown.");
                return;
            }
            string driveTarget = Path.Combine(directory, "drive-relative");
            string rootedTarget = Path.Combine(directory, "current-drive-rooted");
            string driveRelative = root[..2] + Path.GetRelativePath(Environment.CurrentDirectory, driveTarget);
            string currentDriveRooted = rootedTarget[2..];
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    Directory::CreateDirectory(argv[1]);
                    Directory::CreateDirectory(argv[2]);
                    if (!Directory::Exists(argv[1]) || !Directory::Exists(argv[2])) return 1;
                    return 0;
                }
                """, optimization);
            Check(executable, [driveRelative, currentDriveRooted]);
            Assert.True(Directory.Exists(driveTarget));
            Assert.True(Directory.Exists(rootedTarget));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FollowsDirectoryLinksAndRejectsDanglingLinks(int optimization)
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("The symbolic-link case runs on Unix without additional privileges."); return; }
        string directory = CreateDirectory();
        try
        {
            string target = Path.Combine(directory, "target"), alias = Path.Combine(directory, "alias"), dangling = Path.Combine(directory, "dangling");
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(alias, "target");
            Directory.CreateSymbolicLink(dangling, "missing-target");
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    Directory::CreateDirectory(argv[1]);
                    try { Directory::CreateDirectory(argv[2]); return 1; } catch (IOException* error) {}
                    return 0;
                }
                """, optimization);
            Check(executable, [Path.Combine(alias, "child"), Path.Combine(dangling, "child")]);
            Assert.True(Directory.Exists(Path.Combine(target, "child")));
            Assert.False(Directory.Exists(Path.Combine(directory, "missing-target")));
            Assert.Equal("target", new DirectoryInfo(alias).LinkTarget);
            Assert.Equal("missing-target", new DirectoryInfo(dangling).LinkTarget);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PermissionFailuresLeaveDeniedParentsUnchanged(int optimization)
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("The permission case uses Unix mode bits."); return; }
        string directory = CreateDirectory();
        string parent = Path.Combine(directory, "protected");
        Directory.CreateDirectory(parent);
        UnixFileMode original = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            string probe = Path.Combine(parent, "host-probe");
            bool denied = false;
            try { Directory.CreateDirectory(probe); }
            catch (UnauthorizedAccessException) { denied = true; }
            if (!denied) { output.WriteLine("The current account bypasses directory write permissions."); return; }
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    try { Directory::CreateDirectory(argv[1]); return 1; } catch (IOException* error) {}
                    return 0;
                }
                """, optimization);
            string destination = Path.Combine(parent, "child");
            Check(executable, [destination]);
            Assert.False(Directory.Exists(Path.Combine(parent, "child")));
        }
        finally
        {
            File.SetUnixFileMode(parent, original);
            await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-directory-create-" + Guid.NewGuid().ToString("N"));
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
            .Append(new SourceFile("directory-create.gf", code));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
