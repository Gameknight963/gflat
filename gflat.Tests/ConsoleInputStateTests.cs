namespace gflat.Tests;

public sealed class ConsoleInputStateTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string program, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-console-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "input.bin");
            File.WriteAllBytes(path, [65, 0, 66, 10]);
            program = program.Replace("$PATH", Literal(path));
            var sources = new[] { "core/IOException.gf", "core/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "libc/Console/Console.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program));
            string ir = Path.Combine(directory, "input.ll"), executable = Path.Combine(directory, "input.exe");
            File.WriteAllText(ir, Compiler.Emit(sources));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            Assert.Empty(result.StandardOutput);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClearingEofPermitsNewFileDataAndPreservesBufferedAndPushedBackBytes(int optimization)
    {
        Run("""
            extern void* __gflat_stdio_stdin();
            extern void*? freopen(readonly(char*) path, readonly(char*) mode, void* stream);
            extern void*? fopen(readonly(char*) path, readonly(char*) mode);
            extern int fclose(void* stream);
            extern int fputc(int value, void* stream);
            extern int feof(void* stream);
            extern int ferror(void* stream);
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                public replace void Free(void* data) { }
            }
            int main() {
                void* stream = __gflat_stdio_stdin();
                if (freopen($PATH, "rb", stream) == null) return 1;
                if (Console::ReadByte() != 65 || Console::PeekByte() != 0) return 2;
                Console::ClearInputError();
                if (Console::PeekByte() != 0 || Console::ReadByte() != 0) return 3;
                char[2] data;
                Console::ReadExactly(data, 2);
                if (data[0] != 'B' || data[1] != '\n' || Console::ReadByte() != -1 || feof(stream) == 0) return 4;
                void*? append = fopen($PATH, "ab");
                if (append == null) return 5;
                if (fputc(255, append!) != 255 || fclose(append!) != 0) return 6;
                // Clear the observed EOF before retrying for newly appended data.
                if (feof(stream) == 0) return 7;
                Console::ClearInputError();
                if (feof(stream) != 0 || ferror(stream) != 0 || Console::ReadByte() != 255) return 8;
                if (Console::ReadByte() != -1 || feof(stream) == 0) return 9;
                Console::ClearInputError();
                if (feof(stream) != 0 || Calls::Allocations != 0) return 10;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClearingReadErrorOnlyResetsIndicatorsAndDoesNotRepairTheStreamMode(int optimization)
    {
        Run("""
            extern void* __gflat_stdio_stdin();
            extern void*? freopen(readonly(char*) path, readonly(char*) mode, void* stream);
            extern int feof(void* stream);
            extern int ferror(void* stream);
            int main() {
                void* stream = __gflat_stdio_stdin();
                if (freopen($PATH, "wb", stream) == null) return 1;
                for (int i = 0; i < 2; i++) {
                    try { Console::ReadByte(); return 2; }
                    catch (std::EndOfStreamException* error) { return 3; }
                    catch (std::IOException* error) { }
                    if (ferror(stream) == 0) return 4;
                    Console::ClearInputError();
                    if (ferror(stream) != 0 || feof(stream) != 0) return 5;
                }
                return 0;
            }
            """, optimization);
    }
}
