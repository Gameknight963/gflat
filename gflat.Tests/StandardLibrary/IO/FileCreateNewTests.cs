using Xunit.Abstractions;

namespace gflat.Tests.Std.IO;

public sealed class FileCreateNewTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public async Task ExclusiveCreationWritesBinaryAndPreservesEveryExistingDestination(int optimization, bool facade)
    {
        string directory = CreateDirectory();
        try
        {
            string created = Path.Combine(directory, "new binary.bin"), existing = Path.Combine(directory, "existing.bin");
            string empty = Path.Combine(directory, "empty.bin"), missing = Path.Combine(directory, "missing parent", "child.bin");
            byte[] original = [0, 255, 13, 10, 26, 65];
            File.WriteAllBytes(existing, original);
            string factory = facade ? "File::CreateNew" : "FileStream::CreateNew";
            string executable = Compile(directory, $$"""
                using Standard::IO;

                int main(int argc, char** argv) {
                    FileStream created = {{factory}}(argv[1]);
                    if (!created.IsOpen || created.Position != 0l) return 1;
                    try { FileStream duplicate = {{factory}}(argv[1]); return 2; } catch (IOException* error) {}
                    char[5] bytes = ['\0', '\n', '\r', (char)26, (char)255];
                    created.Write(bytes, 5);
                    created.Close();
                    if (created.IsOpen) return 3;
                    for (int index = 1; index <= 4; index++) {
                        try { FileStream rejected = {{factory}}(argv[index]); return 4 + index; } catch (IOException* error) {}
                    }
                    FileStream empty = {{factory}}(argv[5]);
                    empty.Close();
                    FileStream input = FileStream::OpenRead(argv[1]);
                    for (nuint index = 0; index < 5; index++)
                        if (input.ReadByte() != (int)(byte)bytes[index]) return 9;
                    if (input.ReadByte() != -1) return 10;
                    input.Close();
                    return 0;
                }
                """, optimization, facade);
            var result = NativeToolchain.Run(executable, [created, existing, directory, missing, empty], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal(new byte[] { 0, 10, 13, 26, 255 }, File.ReadAllBytes(created));
            Assert.Equal(original, File.ReadAllBytes(existing));
            Assert.Empty(File.ReadAllBytes(empty));
            Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ExclusiveCreationRejectsExistingAndDanglingSymbolicLinks(int optimization)
    {
        if (OperatingSystem.IsWindows())
        {
            output.WriteLine("The symbolic-link case runs on Unix without additional privileges.");
            return;
        }
        string directory = CreateDirectory();
        try
        {
            string target = Path.Combine(directory, "target.bin"), targetDirectory = Path.Combine(directory, "target-directory");
            File.WriteAllBytes(target, [0, 255, 65]);
            Directory.CreateDirectory(targetDirectory);
            string fileLink = Path.Combine(directory, "file-link"), directoryLink = Path.Combine(directory, "directory-link");
            string dangling = Path.Combine(directory, "dangling-link"), loop = Path.Combine(directory, "loop-link");
            File.CreateSymbolicLink(fileLink, "target.bin");
            Directory.CreateSymbolicLink(directoryLink, "target-directory");
            File.CreateSymbolicLink(dangling, "missing-target");
            File.CreateSymbolicLink(loop, "loop-link");
            string executable = Compile(directory, """
                using Standard::IO;

                int main(int argc, char** argv) {
                    for (int index = 1; index < argc; index++) {
                        try { FileStream output = FileStream::CreateNew(argv[index]); return index; } catch (IOException* error) {}
                    }
                    return 0;
                }
                """, optimization, false);
            var result = NativeToolchain.Run(executable, [fileLink, directoryLink, dangling, loop], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal(new byte[] { 0, 255, 65 }, File.ReadAllBytes(target));
            Assert.False(File.Exists(Path.Combine(directory, "missing-target")));
            Assert.Equal("target.bin", new FileInfo(fileLink).LinkTarget);
            Assert.Equal("target-directory", new DirectoryInfo(directoryLink).LinkTarget);
            Assert.Equal("missing-target", new FileInfo(dangling).LinkTarget);
            Assert.Equal("loop-link", new FileInfo(loop).LinkTarget);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-create-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Compile(string directory, string code, int optimization, bool facade)
    {
        string[] sources = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/IByteReader.gf",
            "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf"];
        IEnumerable<string> selected = sources;
        if (facade) selected = FileApiTestSources.WithConveniences(sources);
        var files = selected.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("file-create-new.gf", code));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
