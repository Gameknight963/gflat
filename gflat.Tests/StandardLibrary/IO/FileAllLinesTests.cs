using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class FileAllLinesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ReadAllLinesPreservesCountedContentsAndLineBoundaries(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::Collections;
                using Standard::IO;
                using Standard::Text;

                extern int printf(readonly(char)* format, ...);
                int main(int argc, char** argv) {
                    Vector<String> lines = File::ReadAllLines(argv[1]);
                    File::WriteAllLines(argv[2], ref lines);
                    printf("%u", (uint)lines.Count);
                    return 0;
                }
                """, optimization);
            byte[][] cases = [[], [10], [13, 10], [10, 10], [65, 10], [65],
                [13], [65, 13, 66, 0, 255, 13, 10, 10, 67, 13],
                Encoding.UTF8.GetBytes(new string('x', 255) + "\r\r\n" + new string('y', 513) + "\n")];
            string input = Path.Combine(directory, "input.bin"), output = Path.Combine(directory, "output.bin");
            foreach (byte[] contents in cases)
            {
                File.WriteAllBytes(input, contents);
                List<byte[]> expected = SplitLines(contents);
                var result = NativeToolchain.Run(executable, [input, output], 30000);
                Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
                Assert.Equal(expected.Count.ToString(), result.StandardOutput);
                Assert.Equal(expected.SelectMany(line => line.Append((byte)10)).ToArray(), File.ReadAllBytes(output));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WriteAndAppendBorrowTheirInputsAndUseExplicitLf(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::Collections;
                using Standard::IO;
                using Standard::Memory;
                using Standard::Text;

                extern void*? malloc(nuint bytes);
                extern void free(void* memory);
                static class State { public static int Allocations; }
                namespace Memory {
                    public replace void*? Allocate(nuint bytes) { State::Allocations++; return malloc(bytes); }
                    public replace void Free(void* memory) nothrow { free(memory); }
                }
                int main(int argc, char** argv) {
                    Vector<String> lines = new();
                    lines.Add(String::From("A\0B", 3));
                    lines.Add(new String());
                    lines.Add(String::From("C\rD"));
                    nuint capacity = lines.Capacity;
                    Span<immutable(String)> view = lines.AsImmutable();
                    immutable(String)* address = view.Data!;
                    int allocations = State::Allocations;
                    File::WriteAllLines(argv[1], ref lines);
                    File::WriteAllLines(argv[2], view);
                    File::WriteAllLines(argv[3], address, lines.Count);
                    File::AppendAllLines(argv[4], ref lines);
                    File::AppendAllLines(argv[5], view);
                    File::AppendAllLines(argv[6], address, lines.Count);
                    if (State::Allocations != allocations || lines.Count != 3 || lines.Capacity != capacity) return 1;
                    if (lines.AsImmutable().Data != address || lines[0].Length != 3 || lines[0].Data[1] != '\0') return 2;
                    Span<immutable(String)> empty = new();
                    File::WriteAllLines(argv[7], empty);
                    File::AppendAllLines(argv[8], empty);
                    File::AppendAllLines(argv[4], empty);
                    if (State::Allocations != allocations) return 3;
                    return 0;
                }
                """, optimization);
            string[] paths = Enumerable.Range(1, 8).Select(index => Path.Combine(directory, $"output-{index}.bin")).ToArray();
            for (int index = 0; index < 7; index++) File.WriteAllText(paths[index], "prefix");
            var result = NativeToolchain.Run(executable, paths, 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            byte[] written = [65, 0, 66, 10, 10, 67, 13, 68, 10];
            for (int index = 0; index < 3; index++) Assert.Equal(written, File.ReadAllBytes(paths[index]));
            for (int index = 3; index < 6; index++) Assert.Equal(Encoding.UTF8.GetBytes("prefix").Concat(written).ToArray(), File.ReadAllBytes(paths[index]));
            Assert.Empty(File.ReadAllBytes(paths[6]));
            Assert.Empty(File.ReadAllBytes(paths[7]));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AllocationFailuresReleaseCollectedLinesAndCloseTheInput(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard;
                using Standard::Collections;
                using Standard::IO;
                using Standard::Text;

                extern void*? malloc(nuint bytes);
                extern void free(void* memory);
                static class State {
                    public static int Calls;
                    public static int FailAt;
                    public static int Failures;
                    public static int Live;
                }
                namespace Memory {
                    public replace void*? Allocate(nuint bytes) {
                        State::Calls++;
                        if (State::Calls == State::FailAt) { State::Failures++; return null; }
                        void*? result = malloc(bytes);
                        if (result != null) State::Live++;
                        return result;
                    }
                    public replace void Free(void* memory) nothrow { State::Live--; free(memory); }
                }
                int main(int argc, char** argv) {
                    State::FailAt = 2;
                    try { Vector<String> lines = File::ReadAllLines(argv[1]); return 1; }
                    catch (BadAllocException* error) {}
                    if (State::Failures != 1) return 2;
                    if (State::Live != 0) return 12;
                    State::Calls = 0;
                    State::Failures = 0;
                    State::FailAt = 4;
                    try { Vector<String> lines = File::ReadAllLines(argv[1]); return 3; }
                    catch (BadAllocException* error) {}
                    if (State::Failures != 1) return 4;
                    if (State::Live != 0) return 14;
                    File::Remove(argv[1]);
                    return 0;
                }
                """, optimization);
            string path = Path.Combine(directory, "lines.txt");
            File.WriteAllText(path, "alpha\nbeta\ngamma\n");
            var result = NativeToolchain.Run(executable, [path], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.False(File.Exists(path));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task OpenFailuresPreserveBorrowedLinesAndReportIoErrors(int optimization)
    {
        string directory = CreateDirectory();
        try
        {
            string executable = Compile(directory, """
                using Standard::Collections;
                using Standard::IO;
                using Standard::Text;

                int main(int argc, char** argv) {
                    Vector<String> lines = new();
                    lines.Add(String::From("kept"));
                    try { File::WriteAllLines(argv[1], ref lines); return 1; } catch (IOException* error) {}
                    try { File::AppendAllLines(argv[1], ref lines); return 2; } catch (IOException* error) {}
                    try { Vector<String> missing = File::ReadAllLines(argv[2]); return 3; } catch (IOException* error) {}
                    if (lines.Count != 1 || lines[0].Length != 4 || lines[0].Data[0] != 'k') return 4;
                    return 0;
                }
                """, optimization);
            var result = NativeToolchain.Run(executable, [directory, Path.Combine(directory, "missing")], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static List<byte[]> SplitLines(byte[] contents)
    {
        var result = new List<byte[]>();
        int start = 0;
        for (int index = 0; index < contents.Length; index++)
        {
            if (contents[index] != 10) continue;
            int end = index > start && contents[index - 1] == 13 ? index - 1 : index;
            result.Add(contents[start..end]);
            start = index + 1;
        }
        if (start < contents.Length) result.Add(contents[start..]);
        return result;
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-all-lines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Compile(string directory, string code, int optimization)
    {
        string[] streamSources = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
            "core/IO/IByteReader.gf", "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf"];
        var sources = FileApiTestSources.WithConveniences(streamSources)
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("file-all-lines.gf", code));
        string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return executable;
    }
}
