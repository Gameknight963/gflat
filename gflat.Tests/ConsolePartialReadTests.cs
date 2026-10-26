namespace gflat.Tests;

public sealed class ConsolePartialReadTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RealIoFailureAfterBufferedPrefixPreservesBytesAndIsNotEndOfStream(int optimization)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
        string directory = Path.Combine(Path.GetTempPath(), "gflat-console-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "input.bin");
            byte[] input = Enumerable.Range(0, 97).Select(index => (byte)(index * 13)).ToArray();
            File.WriteAllBytes(path, input);
            string invalidation = OperatingSystem.IsWindows() ? """
                extern int _fileno(void* stream);
                extern nint _get_osfhandle(int descriptor);
                extern int CloseHandle(void* handle);
                bool Invalidate(void* stream) => CloseHandle((void*)_get_osfhandle(_fileno(stream))) != 0;
                """ : """
                extern int fileno(void* stream);
                extern int close(int descriptor);
                bool Invalidate(void* stream) => close(fileno(stream)) == 0;
                """;
            string program = $$"""
                extern void* __gflat_stdio_stdin();
                extern void*? freopen(immutable(char*) path, immutable(char*) mode, void* stream);
                extern int setvbuf(void* stream, char* data, int mode, nuint capacity);
                extern int ferror(void* stream);
                {{invalidation}}
                int main(int argc, char** argv) {
                    void* stream = __gflat_stdio_stdin();
                    if (freopen({{Literal(path)}}, "rb", stream) == null) return 1;
                    char[256] inputBuffer;
                    // _IOFBF is zero on the Windows/Linux CRTs exercised here.
                    if (setvbuf(stream, inputBuffer, 0, 256) != 0) return 2;
                    if (Console::ReadByte() != 0) return 3;
                    // Keep FILE's unread buffered bytes but break its next OS read.
                    // Windows retains a valid CRT descriptor to avoid invalid-parameter handlers.
                    if (!Invalidate(stream)) return 4;
                    char[130] data;
                    for (nuint i = 0; i < 130; i++) data[i] = 'Z';
                    try {
                        if (argv[1][0] == 'e') Console::ReadExactly(&data[1], 128);
                        else Console::Read(&data[1], 128);
                        return 5;
                    }
                    catch (std::EndOfStreamException* error) { return 6; }
                    catch (std::IOException* error) { }
                    if (ferror(stream) == 0 || data[0] != 'Z') return 7;
                    for (nuint i = 0; i < 96; i++) if ((byte)data[i + 1] != (byte)((i + 1) * 13)) return 8;
                    for (nuint i = 97; i < 130; i++) if (data[i] != 'Z') return 9;
                    return 0;
                }
                """;
            var sources = new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "libc/Console/Console.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program));
            string ir = Path.Combine(directory, "input.ll"), executable = Path.Combine(directory, "input.exe");
            File.WriteAllText(ir, Compiler.Emit(sources));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            foreach (string operation in new[] { "read", "exactly" })
            {
                var result = NativeToolchain.Run(executable, [operation], 30000);
                Assert.True(result.ExitCode == 0, $"{operation}: exit {result.ExitCode}: {result.StandardError}");
                Assert.Empty(result.StandardOutput);
            }
            Assert.Equal(input, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
