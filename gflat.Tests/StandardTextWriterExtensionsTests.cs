namespace gflat.Tests;

public sealed class StandardTextWriterExtensionsTests
{
    private static ExecutionResult Run(string program, string? path = null, bool strings = false, bool console = false)
    {
        var names = new List<string> { "core/TextWriterExtensions.gf", "core/IOException.gf", "core/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "core/BufferWriter.gf", "libc/File.gf" };
        if (strings) names.AddRange(["core/String.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing.gf"]);
        if (console) names.AddRange(["libc/Console/Console.gf", "libc/Console/StandardWriters.gf"]);
        if (path != null) program = program.Replace("$PATH", "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        var result = CompilerTestHelper.Run(sources);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        return result;
    }

    [Fact]
    public void BorrowedBufferWriterUsesAllOverloadsWithoutAllocation()
    {
        Run("""
            using std;
            static class State { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { State::Allocations++; return null; }
                public replace void Free(void* data) { }
            }
            int main() {
                char[32] buffer; BufferWriter storage = new BufferWriter(buffer, 32);
                ITextWriter* writer = &storage;
                writer.WriteLine(); writer.WriteLine("A\0ignored"); writer.WriteLine("B\0C", 3);
                writer.WriteLine("", 0);
                if (storage.Length != 8 || buffer[8] != '\0') return 1;
                readonly(char)* expected = "\nA\nB\0C\n\n";
                for (nuint i = 0; i < storage.Length; i++) if (buffer[i] != expected[i]) return 2;
                if (State::Allocations != 0) return 3;
                return 0;
            }
            """);
    }

    [Fact]
    public void FileAndStringWritersPreserveCountedZeros()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-writer-lines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "lines.bin");
        try
        {
            Run("""
                using std;
                int main() {
                    File file = File::OpenWrite($PATH);
                    ITextWriter* output = &file;
                    output.WriteLine("A\0B", 3); output.WriteLine(); output.WriteLine("end");
                    file.Close();
                    String text = new String(); ITextWriter* stringWriter = &text;
                    stringWriter.WriteLine("A\0B", 3); stringWriter.WriteLine(); stringWriter.WriteLine("end");
                    if (text.Length != 9 || text.Data[1] != '\0' || text.Data[8] != '\n') return 1;
                    File input = File::OpenRead($PATH); char[9] actual; input.ReadExactly(actual, 9);
                    for (nuint i = 0; i < 9; i++) if (actual[i] != text.Data[i]) return 2;
                    return 0;
                }
                """, path, strings: true);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("A\0B\n\nend\n"), File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ConsolePropertiesProvideLineWritingWithoutIntermediateStrings()
    {
        var result = Run("""
            using std;
            int main() {
                Console::Out.WriteLine("out");
                Console::Error.WriteLine("E\0R", 3);
                Console::Error.WriteLine();
                Console::Flush(); Console::FlushError();
                return 0;
            }
            """, console: true);
        // Windows CRT text-mode standard streams translate LF to CRLF.
        Assert.Equal("out\n", result.StandardOutput.Replace("\r\n", "\n"));
        Assert.Equal("E\0R\n\n", result.StandardError.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FailingBodyDoesNotWriteNewlineAndFailingNewlinePreservesBody()
    {
        Run("""
            using std;
            struct RejectingWriter : ITextWriter {
                public int Calls; public int FailAt; public nuint Accepted;
                public RejectingWriter(int failAt) { Calls = 0; FailAt = failAt; Accepted = 0; }
                public void Write(readonly(char)* data, nuint length) {
                    Calls++;
                    if (Calls == FailAt) throw new Exception("write rejected");
                    Accepted += length;
                }
            }
            int main() {
                RejectingWriter first = new RejectingWriter(1); ITextWriter* writer = &first;
                try { writer.WriteLine("abc"); return 1; } catch (Exception* error) { }
                if (first.Calls != 1 || first.Accepted != 0) return 2;
                RejectingWriter second = new RejectingWriter(2); writer = &second;
                try { writer.WriteLine("abc", 3); return 3; } catch (Exception* error) { }
                if (second.Calls != 2 || second.Accepted != 3) return 4;
                char[4] buffer; BufferWriter bounded = new BufferWriter(buffer, 4); writer = &bounded;
                try { writer.WriteLine("abc"); return 5; } catch (BufferTooSmallException* error) { }
                if (bounded.Length != 3 || buffer[3] != '\0') return 6;
                return 0;
            }
            """);
    }
}
