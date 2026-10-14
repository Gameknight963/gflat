namespace gflat.Tests;

public sealed class StdFileTests
{
    private static SourceFile[] Sources(string program) => new[] { "core/IOException.gf", "libc/File.gf" }
        .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
        .Append(new SourceFile("main.gf", program)).ToArray();

    private static string Literal(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string directory, string program)
    {
        string ir = Path.Combine(directory, "file.ll"), exe = Path.Combine(directory, "file.exe");
        System.IO.File.WriteAllText(ir, Compiler.Emit(Sources(program)));
        var compiled = NativeToolchain.Compile(ir, exe);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(exe, [], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void BinaryReadWriteMoveCloseAndAppendPreserveBytes()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "binary.dat");
            Run(directory, $$"""
                using std;
                int main() {
                    File empty = new File(); empty.Close();
                    {
                        File original = File::OpenWrite({{Literal(path)}});
                        File writer = move original;
                        writer.Write("A\0B", 3); writer.Flush();
                        writer.Close(); writer.Close();
                        if (writer.IsOpen) return 1;
                    }
                    { File append = File::OpenAppend({{Literal(path)}}); append.Write("C\0ignored"); }
                    File reader = File::OpenRead({{Literal(path)}});
                    char[8] buffer;
                    if (reader.Read(buffer, 8) != (nuint)4) return 2;
                    if (buffer[0] != 'A' || buffer[1] != '\0' || buffer[2] != 'B' || buffer[3] != 'C') return 3;
                    if (reader.Read(buffer, 8) != (nuint)0) return 4;
                    return 0;
                }
                """);
            Assert.Equal(new byte[] { 65, 0, 66, 67 }, System.IO.File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void OpenFailureAndClosedOperationsThrowIOException()
    {
        WithDirectory(directory => Run(directory, $$"""
            using std;
            int main() {
                try { File missing = File::OpenRead({{Literal(Path.Combine(directory, "missing"))}}); return 1; }
                catch (IOException* error) { }
                File closed = new File();
                try { closed.Write("hello"); return 2; } catch (IOException* error) { }
                char[1] buffer;
                try { closed.Read(buffer, 1); return 3; } catch (IOException* error) { }
                try { closed.Flush(); return 4; } catch (IOException* error) { }
                return 0;
            }
            """));
    }

    [Fact]
    public void OwningFileCannotBeCopied()
    {
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources("""
            int main() { std::File file = new std::File(); std::File copy = file; return 0; }
            """)));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DestructorClosesAndFlushesDuringExceptionUnwinding()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "unwind.dat");
            Run(directory, $$"""
                int main() {
                    try {
                        std::File file = std::File::OpenWrite({{Literal(path)}});
                        file.Write("saved by scope cleanup");
                        throw new Exception("leave scope");
                    } catch (Exception* error) { }
                    return 0;
                }
                """);
            Assert.Equal("saved by scope cleanup", System.IO.File.ReadAllText(path));
        });
    }

    [Fact]
    public void SearchExampleHandlesChunkBoundariesCrLfAndFinalLine()
    {
        WithDirectory(directory =>
        {
            string root = TestSupport.ProjectFileFixture.RepositoryRoot;
            string project = Path.Combine(root, "examples", "search", "search.gfproj");
            var graph = Projects.ProjectGraph.Load(project, p => Projects.ProjectModel.Load(p));
            string ir = Path.Combine(directory, "search.ll"), exe = Path.Combine(directory, "search.exe");
            System.IO.File.WriteAllText(ir, Compiler.Emit(graph.Sources.Select(p => new SourceFile(p, System.IO.File.ReadAllText(p)))));
            var compiled = NativeToolchain.Compile(ir, exe);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string path = Path.Combine(directory, "input.txt");
            string longLine = new string('x', 4095) + "needle";
            System.IO.File.WriteAllText(path, "skip\r\n" + longLine + "\r\nfinal needle\r");
            var result = NativeToolchain.Run(exe, [path, "needle"], 30000);
            Assert.Equal(0, result.ExitCode);
            string newline = OperatingSystem.IsWindows() ? "\r\n" : "\n";
            Assert.Equal(longLine + newline + "final needle\r" + newline, result.StandardOutput);
            Assert.Equal(2, NativeToolchain.Run(exe, [], 30000).ExitCode);
            Assert.Equal(1, NativeToolchain.Run(exe, [path + ".missing", "needle"], 30000).ExitCode);
        });
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
