namespace gflat.Tests.Std.IO;

public sealed class FileBytesTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-bytes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private static void Run(string directory, string program)
    {
        // Deliberately no String, text codecs, or managed collector sources.
        string[] names = ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
            "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf", "core/Memory/Span.gf", "core/Collections/Vector.gf",
            "core/IO/IByteReader.gf", "libc/IO/File.gf", "libc/IO/FileBytes.gf"];
        var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files));
        var compiled = NativeToolchain.Compile(ir, exe);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(exe, [], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(12005)]
    public void ReadAllReturnsOwningBytesAndRoundTripsWithoutTextConversion(int length)
    {
        WithDirectory(directory =>
        {
            string input = Path.Combine(directory, "input.bin"), output = Path.Combine(directory, "output.bin");
            byte[] expected = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            File.WriteAllBytes(input, expected);
            Run(directory, $$"""
                using std; using std::IO; using std::Memory; using std::Queries; using std::Collections;
                int main() {
                    Vector<byte> bytes = FileBytes::ReadAll({{Literal(input)}});
                    if (bytes.Count != (nuint){{length}}) return 1;
                    if (bytes.Count == 0 && bytes.Capacity != 0) return 2;
                    for (nuint i = 0; i < bytes.Count; i++) if (bytes[i] != (byte)i) return 3;
                    Vector<byte> moved = move bytes;
                    Span<readonly(byte)> view = moved.AsImmutable();
                    byte sentinel = 0;
                    readonly(byte)* data = &sentinel;
                    if (view.Length != 0) data = view.Data!;
                    FileBytes::WriteAll({{Literal(output)}}, data, view.Length);
                    File::Remove({{Literal(input)}});
                    return 0;
                }
                """);
            Assert.False(File.Exists(input));
            Assert.Equal(expected, File.ReadAllBytes(output));
        });
    }

    [Fact]
    public void CountedWriteAndAppendPreserveZerosAndZeroCountStillCreatesFile()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "bytes.bin");
            Run(directory, $$"""
                using std; using std::IO; using std::Memory; using std::Queries; using std::Collections;
                int main() {
                    byte[4] data; data[0] = 0; data[1] = 255; data[2] = 128; data[3] = 42;
                    FileBytes::WriteAll({{Literal(path)}}, data, 0);
                    FileBytes::AppendAll({{Literal(path)}}, data, 4);
                    FileBytes::AppendAll({{Literal(path)}}, &data[1], 2);
                    FileBytes::AppendAll({{Literal(path)}}, data, 0);
                    return 0;
                }
                """);
            Assert.Equal(new byte[] { 0, 255, 128, 42, 255, 128 }, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void GrowthAllocationFailureDestroysPartialVectorAndClosesFile()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "failure.bin");
            File.WriteAllBytes(path, new byte[9000]);
            Run(directory, $$"""
                using std; using std::IO; using std::Memory; using std::Queries; using std::Collections;
                extern void*? malloc(nuint size);
                extern void free(void* data);
                static class State { public static void*? Last; public static int Released; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) {
                        if (size == (nuint)8192) return null;
                        void*? result = malloc(size);
                        if (size == (nuint)4096) State::Last = result;
                        return result;
                    }
                    public replace void Free(void* data) nothrow {
                        if (data == State::Last) State::Released++;
                        free(data);
                    }
                }
                int main() {
                    try { Vector<byte> bytes = FileBytes::ReadAll({{Literal(path)}}); return 1; }
                    catch (BadAllocException* error) { }
                    if (State::Last == null || State::Released != 1) return 2;
                    File::Remove({{Literal(path)}});
                    return 0;
                }
                """);
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void LinuxOwningBinaryWritesAndAppendsPropagateDeferredCloseFailure()
    {
        if (!OperatingSystem.IsLinux()) return;
        WithDirectory(directory => Run(directory, """
            using std; using std::IO; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                byte value = (byte)42;
                try { FileBytes::WriteAll("/dev/full", &value, 1); return 1; }
                catch (IOException* error) { }
                try { FileBytes::AppendAll("/dev/full", &value, 1); return 2; }
                catch (IOException* error) { }
                return 0;
            }
            """));
    }

    [Fact]
    public void EmptyInputDoesNotAllocateAndMissingFileReportsIOException()
    {
        WithDirectory(directory =>
        {
            string empty = Path.Combine(directory, "empty.bin"), missing = Path.Combine(directory, "missing.bin");
            File.WriteAllBytes(empty, []);
            Run(directory, $$"""
                using std; using std::IO; using std::Memory; using std::Queries; using std::Collections;
                static class State { public static int Calls; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) { State::Calls++; return null; }
                    public replace void Free(void* data) nothrow { }
                }
                int main() {
                    Vector<byte> bytes = FileBytes::ReadAll({{Literal(empty)}});
                    if (bytes.Count != 0 || bytes.Capacity != 0 || State::Calls != 0) return 1;
                    try { Vector<byte> absent = FileBytes::ReadAll({{Literal(missing)}}); return 2; }
                    catch (IOException* error) { }
                    return 0;
                }
                """);
        });
    }
}
