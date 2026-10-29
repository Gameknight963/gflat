namespace gflat.Tests.Std.IO;

public sealed class FileLinesTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(byte[] contents, Func<string, string> program, bool text = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-lines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "input.bin");
            File.WriteAllBytes(path, contents);
            var names = new List<string> { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf", "core/IO/IByteReader.gf", "libc/IO/File.gf", "libc/IO/FileLines.gf" };
            if (text) names.AddRange(["core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf"]);
            var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program(Literal(path))));
            string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, Compiler.Emit(files));
            var compiled = NativeToolchain.Compile(ir, exe);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(exe, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CallerBuffersHandleCrLfEmptyLinesZerosAndFinalBareCr()
    {
        Run(System.Text.Encoding.UTF8.GetBytes("\r\nhello\nA\0B\rX\r"), path => $$"""
            using std; using std::Text; using std::IO;
            int main() {
                File input = File::OpenRead({{path}});
                char[16] buffer; nuint length = 99;
                if (!FileLines::TryReadLine(&input, buffer, 16, ref length) || length != (nuint)0 || buffer[0] != '\0') return 1;
                if (FileLines::ReadLine(&input, buffer, 16) != (nuint)5 || buffer[4] != 'o' || buffer[5] != '\0') return 2;
                if (!FileLines::TryReadLine(&input, buffer, 16, ref length) || length != (nuint)6) return 3;
                if (buffer[0] != 'A' || buffer[1] != '\0' || buffer[2] != 'B' || buffer[3] != '\r' || buffer[4] != 'X' || buffer[5] != '\r') return 4;
                if (FileLines::TryReadLine(&input, buffer, 16, ref length) || length != (nuint)0 || buffer[0] != '\0') return 5;
                try { FileLines::ReadLine(&input, buffer, 16); return 6; } catch (EndOfStreamException* error) { }
                return 0;
            }
            """);
    }

    [Fact]
    public void OverflowConsumesLineAndKeepsTerminatedPrefixAndCount()
    {
        Run(System.Text.Encoding.UTF8.GetBytes(new string('x', 1000) + "\r\nnext\n\n"), path => $$"""
            using std; using std::Text; using std::IO;
            int main() {
                File input = File::OpenRead({{path}});
                char[5] buffer; nuint length = 99;
                try { FileLines::TryReadLine(&input, buffer, 5, ref length); return 1; }
                catch (BufferTooSmallException* error) { }
                if (length != (nuint)4 || buffer[0] != 'x' || buffer[3] != 'x' || buffer[4] != '\0') return 2;
                if (FileLines::ReadLine(&input, buffer, 5) != (nuint)4 || buffer[0] != 'n' || buffer[3] != 't') return 3;
                if (!FileLines::TryReadLine(&input, buffer, 1, ref length) || length != (nuint)0 || buffer[0] != '\0') return 4;
                if (FileLines::TryReadLine(&input, buffer, 1, ref length)) return 5;
                return 0;
            }
            """);
    }

    [Fact]
    public void ZeroCapacityDoesNotConsumeAndClosedInputThrows()
    {
        Run([65, 10], path => $$"""
            using std; using std::Text; using std::IO;
            int main() {
                File input = File::OpenRead({{path}});
                char[4] buffer; nuint length = 99;
                try { FileLines::TryReadLine(&input, buffer, 0, ref length); return 1; } catch (BufferTooSmallException* error) { }
                if (length != (nuint)0 || FileLines::ReadLine(&input, buffer, 4) != (nuint)1 || buffer[0] != 'A') return 2;
                input.Close();
                try { FileLines::TryReadLine(&input, buffer, 4, ref length); return 3; } catch (IOException* error) { }
                if (length != (nuint)0 || buffer[0] != '\0') return 4;
                return 0;
            }
            """);
    }

    [Fact]
    public void WriterOverloadAppendsToOwningStringAcrossChunks()
    {
        byte[] bytes = Enumerable.Repeat((byte)120, 255).Concat(new byte[] { 13, 13, 0, 90, 13, 10 }).ToArray();
        Run(bytes, path => $$"""
            using std; using std::Text; using std::IO;
            int main() {
                File input = File::OpenRead({{path}});
                String line = s"prefix:";
                if (!FileLines::TryReadLine(&input, &line)) return 1;
                if (line.Length != (nuint)266 || line.Data[7] != 'x' || line.Data[262] != '\r' || line.Data[263] != '\r' || line.Data[264] != '\0' || line.Data[265] != 'Z') return 2;
                if (FileLines::TryReadLine(&input, &line) || line.Length != (nuint)266) return 3;
                return 0;
            }
            """, text: true);
    }
}
