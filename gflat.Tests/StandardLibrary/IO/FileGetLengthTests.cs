using System.Globalization;
using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class FileGetLengthTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MetadataLengthMatchesFileInfoAcrossWritesResizesRenameAndAccessModes(int optimization)
    {
        string directory = CreateDirectory();
        string file = Path.Combine(directory, "contents.bin");
        try
        {
            string executable = Compile(directory, optimization);
            foreach (int length in new[] { 0, 256, 8193 })
            {
                byte[] bytes = Enumerable.Range(0, length).Select(value => (byte)value).ToArray();
                File.WriteAllBytes(file, bytes);
                CheckLength(executable, file);
                Assert.Equal(bytes, File.ReadAllBytes(file));
            }
            using (var stream = File.OpenWrite(file)) { stream.SetLength(17); }
            CheckLength(executable, file);
            using (var stream = File.OpenWrite(file)) { stream.SetLength(9001); }
            CheckLength(executable, file);
            byte[] original = File.ReadAllBytes(file);
            File.SetAttributes(file, FileAttributes.ReadOnly);
            CheckLength(executable, file);
            UnixFileMode mode = default;
            if (!OperatingSystem.IsWindows()) { mode = File.GetUnixFileMode(file); File.SetUnixFileMode(file, UnixFileMode.None); }
            try { CheckLength(executable, file); }
            finally { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, mode); }
            File.SetAttributes(file, FileAttributes.Normal);
            Assert.Equal(original, File.ReadAllBytes(file));
            string renamed = Path.Combine(directory, "renamed.bin");
            File.Move(file, renamed);
            CheckFailure(executable, file);
            CheckLength(executable, renamed);
            Assert.Equal(original, File.ReadAllBytes(renamed));
            CheckFailure(executable, directory);
            CheckFailure(executable, "");
            CheckFailure(executable, Path.Combine(directory, "missing-parent", "child"));
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
    public async Task MetadataQueriesFollowFileLinksAndRejectOtherTargets(int optimization)
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("The symbolic-link case runs on Unix without additional privileges."); return; }
        string directory = CreateDirectory();
        try
        {
            string file = Path.Combine(directory, "target.bin"), alias = Path.Combine(directory, "alias");
            byte[] bytes = [0, 255, 65, 13, 10];
            File.WriteAllBytes(file, bytes);
            File.CreateSymbolicLink(alias, "target.bin");
            string directoryLink = Path.Combine(directory, "directory-link"), dangling = Path.Combine(directory, "dangling");
            Directory.CreateSymbolicLink(directoryLink, ".");
            File.CreateSymbolicLink(dangling, "missing");
            string loop = Path.Combine(directory, "loop");
            File.CreateSymbolicLink(loop, "loop");
            string executable = Compile(directory, optimization);
            CheckLength(executable, alias);
            CheckFailure(executable, directoryLink);
            CheckFailure(executable, dangling);
            CheckFailure(executable, loop);
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Equal("target.bin", new FileInfo(alias).LinkTarget);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static void CheckLength(string executable, string path)
    {
        long expected = new FileInfo(path).Length;
        var result = NativeToolchain.Run(executable, ["get", path], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        Assert.Equal(expected, long.Parse(result.StandardOutput, CultureInfo.InvariantCulture));
    }

    private static void CheckFailure(string executable, string path)
    {
        var result = NativeToolchain.Run(executable, ["error", path], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-get-length-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Compile(string directory, int optimization)
    {
        string[] selected = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf",
            "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf"];
        var sources = FileApiTestSources.WithConveniences(selected)
            .Select(path => new SourceFile(path, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", path))))
            .Append(new SourceFile("file-get-length.gf", """
                using Standard::IO;

                extern int printf(readonly(char)* format, ...);
                static class State { public static int Allocations; }
                namespace Memory {
                    public replace void*? Allocate(nuint bytes) { State::Allocations++; return null; }
                    public replace void Free(void* data) nothrow {}
                }
                int main(int argc, char** argv) {
                    if (argv[1][0] == 'e') {
                        try { File::GetLength(argv[2]); return 1; } catch (IOException* error) {}
                    } else {
                        long length = File::GetLength(argv[2]);
                        if (State::Allocations != 0) return 2;
                        printf("%lld", length);
                    }
                    return 0;
                }
                """));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        string text = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        Assert.Matches("(?m)^declare i64 @\"?__gflat_stdio_file_length\"?\\(", text);
        File.WriteAllText(ir, text);
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
