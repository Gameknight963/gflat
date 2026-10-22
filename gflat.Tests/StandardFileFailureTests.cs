namespace gflat.Tests;

public sealed class StandardFileFailureTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private static void Run(string directory, string program, bool strings = false)
    {
        var names = new List<string> { "core/IOException.gf", "core/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "libc/File.gf", "libc/File/FileLines.gf" };
        if (strings) names.AddRange(["libc/File/FileText.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Formatting.gf"]);
        var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files));
        var compiled = NativeToolchain.Compile(ir, exe);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(exe, [], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void ReadFailuresAreNotMistakenForEndOfFileAndErrorsCanBeCleared()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "mode-errors.bin");
            Run(directory, $$"""
                using std;
                int main() {
                    File output = File::OpenWrite({{Literal(path)}});
                    try { output.ReadByte(); return 1; }
                    catch (EndOfStreamException* error) { return 2; }
                    catch (IOException* error) { }
                    if (output.EndOfFile) return 3;
                    output.ClearError();
                    char[4] buffer;
                    try { output.ReadExactly(buffer, 1); return 4; }
                    catch (EndOfStreamException* error) { return 5; }
                    catch (IOException* error) { }
                    output.ClearError(); output.Write("abc"); output.Close();
                    File input = File::OpenRead({{Literal(path)}});
                    try { input.Write("x"); return 6; } catch (IOException* error) { }
                    input.ClearError(); input.ReadExactly(buffer, 3);
                    if (buffer[0] != 'a' || buffer[1] != 'b' || buffer[2] != 'c') return 7;
                    return 0;
                }
                """);
            Assert.Equal("abc", File.ReadAllText(path));
        });
    }

    [Fact]
    public void WholeFileSecondAllocationFailureFreesPriorBufferAndClosesStream()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "allocation.bin");
            File.WriteAllText(path, new string('x', 9000));
            Run(directory, $$"""
                using std;
                extern void*? malloc(nuint size);
                extern void free(void* data);
                static class State { public static void*? First; public static int Released; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) {
                        if (size == (nuint)8193) return null;
                        void*? data = malloc(size);
                        if (size == (nuint)4097) State::First = data;
                        return data;
                    }
                    public replace void Free(void* data) {
                        if (data == State::First) State::Released++;
                        free(data);
                    }
                }
                int main() {
                    try { String text = FileText::ReadAll({{Literal(path)}}); return 1; }
                    catch (BadAllocException* error) { }
                    if (State::First == null || State::Released != 1) return 2;
                    File::Remove({{Literal(path)}});
                    return 0;
                }
                """, strings: true);
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void WriterFailureLeavesBorrowedFileOpenAtPartiallyConsumedLine()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "writer-failure.txt");
            File.WriteAllText(path, new string('x', 300) + "\nnext\n");
            Run(directory, $$"""
                using std;
                struct FailingWriter : ITextWriter {
                    public void Write(readonly(char)* text, nuint count) { throw new Exception("writer rejected bytes"); }
                }
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    FailingWriter output = new FailingWriter();
                    try { FileLines::TryReadLine(&input, &output); return 1; } catch (Exception* error) { }
                    if (!input.IsOpen) return 2;
                    char[64] buffer;
                    if (FileLines::ReadLine(&input, buffer, 64) != (nuint)44 || buffer[0] != 'x' || buffer[43] != 'x') return 3;
                    if (FileLines::ReadLine(&input, buffer, 64) != (nuint)4 || buffer[0] != 'n' || buffer[3] != 't') return 4;
                    return 0;
                }
                """);
        });
    }

    [Fact]
    public void LineStringAllocationFailurePreservesStringAndBorrowedFile()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "line-allocation.txt");
            File.WriteAllText(path, new string('x', 300) + "\nnext\n");
            Run(directory, $$"""
                using std;
                extern void*? malloc(nuint size);
                extern void free(void* data);
                static class State { public static bool Fail; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) {
                        if (State::Fail && size == (nuint)257) return null;
                        return malloc(size);
                    }
                    public replace void Free(void* data) { free(data); }
                }
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    String line = new String(); State::Fail = true;
                    try { FileLines::TryReadLine(&input, &line); return 1; } catch (BadAllocException* error) { }
                    if (line.Length != (nuint)0 || line.Data[0] != '\0' || !input.IsOpen) return 2;
                    State::Fail = false;
                    if (!FileLines::TryReadLine(&input, &line) || line.Length != (nuint)44) return 3;
                    line.Clear();
                    if (!FileLines::TryReadLine(&input, &line) || line.Length != (nuint)4 || line.Data[0] != 'n') return 4;
                    return 0;
                }
                """, strings: true);
        });
    }

    [Fact]
    public void LinuxDelayedWriteFailuresCloseOnceAndDestructorsDoNotThrow()
    {
        // /dev/full accepts opens but rejects writes. This specifically exercises
        // errors deferred by libc buffering until flush/close on Linux CI.
        if (!OperatingSystem.IsLinux()) return;
        WithDirectory(directory => Run(directory, """
            using std;
            int main() {
                File output = File::OpenWrite("/dev/full");
                bool buffered = false;
                try { output.Write("buffered"); buffered = true; } catch (IOException* error) { }
                if (buffered) {
                    try { output.Close(); return 1; } catch (IOException* error) { }
                    if (output.IsOpen) return 2;
                }
                else { try { output.Close(); } catch (IOException* error) { } }
                output.Close();
                { File scoped = File::OpenWrite("/dev/full"); try { scoped.Write("buffered"); } catch (IOException* error) { } }
                try { FileText::WriteAll("/dev/full", "buffered"); return 3; } catch (IOException* error) { }
                try { FileText::AppendAll("/dev/full", "buffered"); return 4; } catch (IOException* error) { }
                String owned = s"body";
                try { FileText::WriteAll("/dev/full", ref owned); return 5; } catch (IOException* error) { }
                try { FileText::AppendAll("/dev/full", ref owned); return 6; } catch (IOException* error) { }
                if (owned.Length != 4 || owned.Data[0] != 'b') return 7;
                return 0;
            }
            """, strings: true));
    }

    [Fact]
    public void FailedCopyKeepsBothBorrowedFilesOpenAndDoesNotAlterDestination()
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "copy-source.bin"), target = Path.Combine(directory, "copy-target.bin");
            File.WriteAllText(source, "source");
            File.WriteAllText(target, "target");
            Run(directory, $$"""
                using std;
                int main() {
                    File input = File::OpenRead({{Literal(source)}});
                    File output = File::OpenRead({{Literal(target)}});
                    try { input.CopyTo(&output); return 1; } catch (IOException* error) { }
                    if (!input.IsOpen || !output.IsOpen) return 2;
                    if (input.ReadByte() != -1) return 3;
                    output.ClearError();
                    if (output.ReadByte() != 116) return 4;
                    return 0;
                }
                """);
            Assert.Equal("target", File.ReadAllText(target));
        });
    }
}
