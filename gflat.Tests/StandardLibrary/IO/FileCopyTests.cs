using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class FileCopyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CopyPreservesEmptyAndCountedBinaryContentsAcrossBufferBoundaries(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    File::Copy(argv[1], argv[2]);
                    return 0;
                }
                """, optimization);
            foreach (int length in new[] { 0, 1, 4095, 4096, 4097, 16391 })
            {
                byte[] contents = Enumerable.Range(0, length).Select(value => (byte)(value * 73)).ToArray();
                string source = Path.Combine(directory, "source.bin"), destination = Path.Combine(directory, "copy " + length + ".bin");
                File.WriteAllBytes(source, contents);
                Check(executable, [source, destination]);
                Assert.Equal(contents, File.ReadAllBytes(destination));
                Assert.Equal(contents, File.ReadAllBytes(source));
                using var reopened = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ExistingDestinationsAndOpenFailuresPreserveFiles(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string source = Path.Combine(directory, "source.bin"), destination = Path.Combine(directory, "existing.bin");
            byte[] contents = [0, 255, 13, 10, 26], original = [65, 0, 255, 66];
            File.WriteAllBytes(source, contents);
            File.WriteAllBytes(destination, original);
            string missing = Path.Combine(directory, "missing.bin"), absent = Path.Combine(directory, "not-created.bin");
            string missingParent = Path.Combine(directory, "missing-parent", "child.bin");
            string executable = Compile(directory, """
                using Standard::IO;

                bool Rejected(readonly(char)* source, readonly(char)* destination) {
                    try { File::Copy(source, destination); } catch (IOException* error) { return true; }
                    return false;
                }
                int main(int argc, char** argv) {
                    if (!Rejected(argv[1], argv[2])) return 1;
                    if (!Rejected(argv[1], argv[1])) return 2;
                    if (!Rejected(argv[1], argv[3])) return 3;
                    if (!Rejected(argv[4], argv[5])) return 4;
                    if (!Rejected(argv[1], argv[6])) return 5;
                    return 0;
                }
                """, optimization);
            Check(executable, [source, destination, directory, missing, absent, missingParent]);
            Assert.Equal(contents, File.ReadAllBytes(source));
            Assert.Equal(original, File.ReadAllBytes(destination));
            Assert.False(File.Exists(absent));
            Assert.False(Directory.Exists(Path.GetDirectoryName(missingParent)));
            using var reopenedSource = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var reopenedDestination = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ExistingHardLinksAndSymbolicLinksArePreserved(int optimization)
    {
        if (OperatingSystem.IsWindows())
        {
            output.WriteLine("The link case runs on Unix without additional privileges.");
            return;
        }
        string directory = CreateDirectory();
        try
        {
            string source = Path.Combine(directory, "source.bin"), hardLink = Path.Combine(directory, "hard-link");
            byte[] contents = [0, 255, 65];
            File.WriteAllBytes(source, contents);
            Assert.Equal(0, link(source, hardLink));
            string symbolic = Path.Combine(directory, "symbolic-link"), dangling = Path.Combine(directory, "dangling-link");
            File.CreateSymbolicLink(symbolic, "source.bin");
            File.CreateSymbolicLink(dangling, "absent-target");
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    for (int index = 2; index < argc; index++) {
                        try { File::Copy(argv[1], argv[index]); return index; } catch (IOException* error) {}
                    }
                    return 0;
                }
                """, optimization);
            Check(executable, [source, hardLink, symbolic, dangling]);
            Assert.Equal(contents, File.ReadAllBytes(source));
            Assert.Equal(contents, File.ReadAllBytes(hardLink));
            Assert.Equal("source.bin", new FileInfo(symbolic).LinkTarget);
            Assert.Equal("absent-target", new FileInfo(dangling).LinkTarget);
            Assert.False(File.Exists(Path.Combine(directory, "absent-target")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string source, string destination);

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-copy-" + Guid.NewGuid().ToString("N"));
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
        string[] sources = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf",
            "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf"];
        var files = FileApiTestSources.WithConveniences(sources)
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("file-copy.gf", code));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
