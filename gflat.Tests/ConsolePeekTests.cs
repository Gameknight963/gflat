using System.Diagnostics;

namespace gflat.Tests;

public sealed class ConsolePeekTests
{
    private static void Run(string program, byte[] input, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-console-peek-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "write-only.bin").Replace("\\", "\\\\").Replace("\"", "\\\"");
            program = program.Replace("$PATH", "\"" + path + "\"");
            var sources = new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "libc/Console/Console.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program));
            string ir = Path.Combine(directory, "peek.ll"), executable = Path.Combine(directory, "peek.exe");
            File.WriteAllText(ir, Compiler.Emit(sources));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            using var process = new Process { StartInfo = new ProcessStartInfo(executable) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            } };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardInput.BaseStream.Write(input);
            process.StandardInput.Close();
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Console peek test timed out"); }
            Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}: {errors.GetAwaiter().GetResult()}");
            Assert.Equal("", output.GetAwaiter().GetResult());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RepeatedPeeksPreserveUnsignedBytesAndEofWithoutAllocation(int optimization)
    {
        Run("""
            static class State { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint count) { State::Allocations++; return null; }
                public replace void Free(void* data) nothrow { }
            }
            int main() {
                int[4] expected = [65, 0, 255, 10];
                for (int i = 0; i < 4; i++) {
                    for (int attempt = 0; attempt < 20; attempt++)
                        if (Console::PeekByte() != expected[i]) return 1;
                    if (Console::ReadByte() != expected[i]) return 2;
                }
                if (Console::PeekByte() != -1 || Console::PeekByte() != -1 || Console::ReadByte() != -1) return 3;
                if (State::Allocations != 0) return 4;
                return 0;
            }
            """, [65, 0, 255, 10], optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PeekCountedAndLineReadsShareTheSamePosition(int optimization)
    {
        Run("""
            int main() {
                char[16] data; nuint length = 0;
                if (Console::PeekByte() != 65) return 1;
                Console::ReadExactly(data, 2);
                if (data[0] != 'A' || data[1] != '\0') return 2;
                if (Console::PeekByte() != 10 || Console::PeekByte() != 10) return 3;
                if (!Console::TryReadLine(data, 16, ref length) || length != 0 || data[0] != '\0') return 4;
                if (Console::PeekByte() != 116) return 5;
                if (!Console::TryReadLine(data, 16, ref length) || length != 4 || data[0] != 't' || data[3] != 'l') return 6;
                if (Console::PeekByte() != 90 || Console::Read(data, 16) != 1 || data[0] != 'Z') return 7;
                if (Console::PeekByte() != -1 || Console::TryReadLine(data, 16, ref length) || length != 0) return 8;
                return 0;
            }
            """, [65, 0, 10, 116, 97, 105, 108, 13, 10, 90], optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PeeksRespectTheHostsExistingStdinTextMode(int optimization)
    {
        byte[] expected = OperatingSystem.IsWindows() ? [65, 10, 66] : [65, 13, 10, 66, 26, 67];
        Run($$"""
            int main() {
                {{string.Join("\n", expected.Select(value => $"if (Console::PeekByte() != {value} || Console::PeekByte() != {value} || Console::ReadByte() != {value}) return 1;"))}}
                if (Console::PeekByte() != -1 || Console::PeekByte() != -1 || Console::ReadByte() != -1) return 2;
                return 0;
            }
            """, [65, 13, 10, 66, 26, 67], optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OutputOnlyStdinReportsIoErrorsInsteadOfEof(int optimization)
    {
        Run("""
            extern void* __gflat_stdio_stdin();
            extern void*? freopen(immutable(char*) path, immutable(char*) mode, void* stream);
            extern void clearerr(void* stream);
            int main() {
                void* stream = __gflat_stdio_stdin();
                if (freopen($PATH, "wb", stream) == null) return 1;
                for (int i = 0; i < 2; i++) {
                    clearerr(stream);
                    try { Console::PeekByte(); return 2; }
                    catch (std::IO::EndOfStreamException* error) { return 3; }
                    catch (std::IO::IOException* error) { }
                }
                return 0;
            }
            """, [], optimization);
    }
}

