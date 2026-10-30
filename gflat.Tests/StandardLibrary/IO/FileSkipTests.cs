namespace gflat.Tests.Std.IO;

public sealed class FileSkipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SequentialDiscardMatchesHostBytesAndPreservesFailureContracts(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            List<(string Path, int Skip, int Next)> success = [];
            List<(string Path, int Length)> inputs = [];
            var random = new Random(61027);
            foreach (int length in new[] { 0, 1, 2, 255, 4095, 4096, 4097, 8192, 12289 })
            {
                byte[] bytes = new byte[length];
                random.NextBytes(bytes);
                string path = Path.Combine(directory, $"input-{length}.bin");
                File.WriteAllBytes(path, bytes);
                inputs.Add((path, length));
                foreach (int skip in new[] { 0, 1, 255, 4095, 4096, 4097, 8192, length - 1, length }
                    .Where(count => count >= 0 && count <= length).Distinct())
                    success.Add((path, skip, skip == length ? -1 : bytes[skip]));
            }
            string output = Path.Combine(directory, "write-only.bin");
            string code = $$"""
                using Standard; using Standard::IO;
                extern void*? malloc(nuint size); extern void free(void* storage);
                bool deny = false; int attempts = 0;
                namespace Memory {
                    public replace void*? Allocate(nuint size) { attempts++; if (deny) return null; return malloc(size); }
                    public replace void Free(void* storage) nothrow { free(storage); }
                }
                int main() {
                    readonly(char)*[{{success.Count}}] paths = [{{string.Join(",", success.Select(row => Literal(row.Path)))}}];
                    nuint[{{success.Count}}] counts = [{{string.Join(",", success.Select(row => row.Skip))}}];
                    int[{{success.Count}}] next = [{{string.Join(",", success.Select(row => row.Next))}}];
                    for (nuint row = 0; row < {{success.Count}}; row++) {
                        File input = File::OpenRead(paths[row]);
                        int before = attempts; deny = true;
                        input.SkipExactly(counts[row]);
                        if (!input.IsOpen || input.EndOfFile) return 1;
                        if (input.ReadByte() != next[row]) return 2;
                        bool eof = input.EndOfFile;
                        if (eof != (next[row] == -1)) return 3;
                        input.SkipExactly(0);
                        if (input.EndOfFile != eof || attempts != before) return 4;
                        deny = false; input.Close();
                    }
                    readonly(char)*[{{inputs.Count}}] shortPaths = [{{string.Join(",", inputs.Select(row => Literal(row.Path)))}}];
                    nuint[{{inputs.Count}}] lengths = [{{string.Join(",", inputs.Select(row => row.Length))}}];
                    for (nuint row = 0; row < {{inputs.Count}}; row++) {
                        for (int huge = 0; huge < 2; huge++) {
                            File input = File::OpenRead(shortPaths[row]);
                            nuint count = lengths[row] + 1;
                            if (huge != 0) count = (nuint)0 - (nuint)1;
                            try { input.SkipExactly(count); return 5; }
                            catch (ref EndOfStreamException error) { }
                            if (!input.IsOpen || !input.EndOfFile || input.ReadByte() != -1) return 6;
                            input.ClearError();
                            input.SkipExactly(0);
                            if (input.EndOfFile || input.ReadByte() != -1 || !input.EndOfFile) return 7;
                        }
                    }
                    File closed = new File();
                    for (nuint count = 0; count < 2; count++) {
                        try { closed.SkipExactly(count); return 8; }
                        catch (ref EndOfStreamException error) { return 9; }
                        catch (ref IOException error) { }
                    }
                    File output = File::OpenWrite({{Literal(output)}});
                    output.SkipExactly(0);
                    try { output.SkipExactly(1); return 10; }
                    catch (ref EndOfStreamException error) { return 11; }
                    catch (ref IOException error) { }
                    if (!output.IsOpen || output.EndOfFile) return 12;
                    output.ClearError(); output.Write("kept"); output.Close();
                    try { output.SkipExactly(0); return 13; }
                    catch (ref EndOfStreamException error) { return 14; }
                    catch (ref IOException error) { }
                    return 0;
                }
                """;
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("file-skip.gf", code));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"File skip exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal("kept", File.ReadAllText(output));
        }
        catch (Exception error)
        {
            // Preserve the original failure and its fixture for investigation.
            error.Data["FileSkipDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
