using System.IO.Compression;
using System.Text;

namespace gflat.Tests;

public sealed class ZipPackExampleTests
{
    private const int EntryLimit = 16 * 1024 * 1024;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CliCreatesStoredArchivesAndReportsFailuresWithoutReplacingExistingFiles(int optimization)
    {
        await WithDirectory(directory =>
        {
            string executable = Compile(directory, optimization);
            string binary = Path.Combine(directory, "source.bin"), empty = Path.Combine(directory, "empty.bin");
            string text = Path.Combine(directory, "source.txt"), archive = Path.Combine(directory, "package.zip");
            byte[] bytes = Enumerable.Range(0, 9001).Select(index => (byte)(index * 73 + 19)).ToArray();
            byte[] words = Encoding.UTF8.GetBytes("hello\0world\r\n");
            File.WriteAllBytes(binary, bytes); File.WriteAllBytes(empty, []); File.WriteAllBytes(text, words);
            var packed = Run(executable, [archive, "data/items.bin", binary, "empty", empty, "readme.txt", text]);
            Assert.Equal(0, packed.ExitCode);
            Assert.Equal("Packed 3 entries" + Environment.NewLine, packed.StandardOutput);
            Assert.Empty(packed.StandardError);
            ReadArchive(archive, [("data/items.bin", bytes), ("empty", []), ("readme.txt", words)]);
            byte[] originalArchive = File.ReadAllBytes(archive);
            var existing = Run(executable, [archive, "changed", empty]);
            Assert.Equal(1, existing.ExitCode); Assert.Empty(existing.StandardOutput);
            Assert.Equal(originalArchive, File.ReadAllBytes(archive));
            var sameAsInput = Run(executable, [binary, "source", binary]);
            Assert.Equal(1, sameAsInput.ExitCode); Assert.Equal(bytes, File.ReadAllBytes(binary));

            string unused = Path.Combine(directory, "unused.zip");
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { unused },
                new[] { unused, "name" }, new[] { unused, "name", binary, "dangling" } })
            {
                var bad = Run(executable, arguments);
                Assert.Equal(2, bad.ExitCode); Assert.Contains("usage: zip-pack", bad.StandardError);
                Assert.Empty(bad.StandardOutput); Assert.False(File.Exists(unused));
            }

            string missing = Path.Combine(directory, "missing.bin"), partial = Path.Combine(directory, "missing.zip");
            var absent = Run(executable, [partial, "missing", missing]);
            Assert.Equal(1, absent.ExitCode); Assert.Contains(missing, absent.StandardError);
            Assert.Empty(absent.StandardOutput); Assert.True(File.Exists(partial));
            Assert.Equal(0, new FileInfo(partial).Length);
            string emptyName = Path.Combine(directory, "invalid-name.zip");
            var invalidName = Run(executable, [emptyName, "", binary]);
            Assert.Equal(1, invalidName.ExitCode); Assert.Contains("ZIP entry name", invalidName.StandardError);

            string tooLarge = Path.Combine(directory, "oversized.bin"), limited = Path.Combine(directory, "limited.zip");
            using (var file = File.Create(tooLarge)) file.SetLength(EntryLimit + 1L);
            var exceeded = Run(executable, [limited, "large", tooLarge]);
            Assert.Equal(1, exceeded.ExitCode); Assert.Contains("maximum byte count", exceeded.StandardError);
            Assert.Empty(exceeded.StandardOutput); Assert.Equal(0, new FileInfo(limited).Length);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Utf8EntryNamesRoundTripThroughDotNetAndGflatReaders(int optimization)
    {
        await WithDirectory(directory =>
        {
            string executable = Compile(directory, optimization, utf8Driver: true);
            string input = Path.Combine(directory, "input.bin"), output = Path.Combine(directory, "utf8.zip");
            byte[] bytes = [0, 255, 128, 13, 10, 42];
            File.WriteAllBytes(input, bytes);
            var result = Run(executable, [output, input]);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("Packed 1 entry" + Environment.NewLine, result.StandardOutput);
            ReadArchive(output, [("café/雪.bin", bytes)]);
            string invalid = Path.Combine(directory, "invalid-utf8.zip");
            var rejected = Run(executable, [invalid, input, "invalid"]);
            Assert.Equal(1, rejected.ExitCode);
            Assert.Contains("Invalid UTF-8 ZIP entry name", rejected.StandardError);
            Assert.Empty(rejected.StandardOutput); Assert.Equal(0, new FileInfo(invalid).Length);
        });
    }

    [Fact]
    public async Task ArchiveLimitIncludesHeadersAndNamesAsWellAsEntryBytes()
    {
        await WithDirectory(directory =>
        {
            string executable = Compile(directory, 2);
            string input = Path.Combine(directory, "block.bin"), output = Path.Combine(directory, "large.zip");
            using (var file = File.Create(input)) file.SetLength(EntryLimit);
            var arguments = new List<string> { output };
            for (int index = 0; index < 8; index++) { arguments.Add("part" + index + ".bin"); arguments.Add(input); }
            var result = Run(executable, arguments.ToArray(), 120000);
            Assert.Equal(1, result.ExitCode); Assert.Contains("ZIP archive exceeds 128 MiB", result.StandardError);
            Assert.Empty(result.StandardOutput);
            // Seven local headers and payloads were written before the eighth exceeded the archive budget.
            Assert.Equal(7L * (EntryLimit + 30 + "part0.bin".Length), new FileInfo(output).Length);
            Assert.Throws<InvalidDataException>(() => { using var archive = ZipFile.OpenRead(output); });
        });
    }

    private static string Compile(string directory, int optimization, bool utf8Driver = false)
    {
        string path = Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "examples", "zip-pack", "main.gf");
        string program = File.ReadAllText(path);
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).ToList();
        if (utf8Driver)
        {
            const string signature = "int main(int argc, char** argv)";
            Assert.Contains(signature, program);
            program = program.Replace(signature, "int PackMain(int argc, char** argv)", StringComparison.Ordinal);
            sources.Add(new SourceFile("utf8-entry-driver.gf", """
                using Standard::Collections;
                using Standard::Compression;
                using Standard::IO;

                int main(int argc, char** argv)
                {
                    char[] name = "café/雪.bin";
                    if (argc > 3) name[0] = (char)255;
                    char*[4] arguments = [argv[0], argv[1], name, argv[2]];
                    int status = PackMain(4, arguments);
                    if (status != 0) return status;
                    Vector<byte> image = File::ReadAllBytes(argv[1], 134217728);
                    ZipArchive archive = new(image.AsReadOnly());
                    if (archive.Count != 1) return 90;
                    ZipArchiveEntry entry = archive.GetEntry(name);
                    if (!entry.IsUtf8Name || entry.Method != 0) return 91;
                    Vector<byte> restored = entry.ReadAllBytes(16777216);
                    Vector<byte> original = File::ReadAllBytes(argv[2], 16777216);
                    if (restored.Count != original.Count) return 92;
                    for (nuint index = 0; index < original.Count; index++)
                        if (restored[index] != original[index]) return 93;
                    return 0;
                }
                """));
        }
        sources.Add(new SourceFile(path, program));
        string ir = Path.Combine(directory, "zip-pack.ll"), executable = Path.Combine(directory, "zip-pack.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }

    private static ProcessResult Run(string executable, string[] arguments, int timeout = 30000)
        => NativeToolchain.Run(executable, arguments, timeout);

    private static void ReadArchive(string path, (string Name, byte[] Bytes)[] expected)
    {
        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(expected.Select(item => item.Name), archive.Entries.Select(entry => entry.FullName));
        for (int index = 0; index < expected.Length; index++)
        {
            ZipArchiveEntry entry = archive.Entries[index];
            Assert.Equal(expected[index].Bytes.LongLength, entry.Length);
            Assert.Equal(entry.Length, entry.CompressedLength);
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            Assert.Equal(expected[index].Bytes, bytes.ToArray());
        }
    }

    private static async Task WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-zip-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool passed = false;
        try { action(directory); passed = true; }
        catch (Exception error) { error.Data["ZipPackDirectory"] = directory; throw; }
        finally { if (passed) await TestDirectoryCleanup.DeleteAsync(directory); }
    }
}
