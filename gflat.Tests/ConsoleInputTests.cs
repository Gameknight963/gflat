using System.Diagnostics;

namespace gflat.Tests;

public sealed class ConsoleInputTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string program, byte[] input, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-console-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            program = program.Replace("$PATH", Literal(Path.Combine(directory, "write-only.bin")));
            var sources = new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "libc/Console/Console.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program));
            string ir = Path.Combine(directory, "input.ll"), executable = Path.Combine(directory, "input.exe");
            File.WriteAllText(ir, Compiler.Emit(sources));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            using var process = new Process { StartInfo = new ProcessStartInfo(executable) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            } };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            process.StandardInput.BaseStream.Write(input); process.StandardInput.Close();
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Console input test timed out"); }
            Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}: {errors.GetAwaiter().GetResult()}");
            Assert.Equal("", output.GetAwaiter().GetResult());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedReadsPreserveNullsAndUnsignedBytesWithoutAllocation(int optimization)
    {
        Run("""
            static class State { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint count) { State::Allocations++; return null; }
                public replace void Free(void* data) nothrow { }
            }
            int main() {
                char[6] data = ['Z','Z','Z','Z','Z','Z'];
                char*? absent = null;
                if (Console::Read(absent!, 0) != 0) return 1;
                Console::ReadExactly(absent!, 0);
                if (Console::ReadByte() != 65) return 2;
                Console::ReadExactly(&data[1], 2);
                if (data[0] != 'Z' || data[1] != '\0' || (byte)data[2] != (byte)255 || data[3] != 'Z') return 3;
                if (Console::Read(&data[1], 4) != 1 || data[1] != 'B' || (byte)data[2] != (byte)255 || data[5] != 'Z') return 4;
                if (Console::ReadByte() != -1 || Console::Read(data, 6) != 0) return 5;
                if (State::Allocations != 0) return 6;
                return 0;
            }
            """, [65, 0, 255, 66], optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnexpectedEofPreservesConsumedPrefixAndThrowsEndOfStream(int optimization)
    {
        Run("""
            int main() {
                char[5] data = ['Z','Z','Z','Z','Z'];
                try { Console::ReadExactly(&data[1], 3); return 1; }
                catch (std::IO::EndOfStreamException* error) { }
                if (data[0] != 'Z' || data[1] != 'A' || data[2] != '\0' || data[3] != 'Z' || data[4] != 'Z') return 2;
                if (Console::ReadByte() != -1) return 3;
                try { Console::ReadExactly(data, 1); return 4; } catch (std::IO::EndOfStreamException* error) { }
                return 0;
            }
            """, [65, 0], optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndLineReadsShareTheSameBorrowedStdinPosition(int optimization)
    {
        Run("""
            int main() {
                char[16] data; nuint length = 0;
                Console::ReadExactly(data, 2);
                if (data[0] != 'Q' || data[1] != '\0' || Console::ReadByte() != 82) return 1;
                if (!Console::TryReadLine(data, 16, ref length) || length != 4 || data[0] != 't' || data[3] != 'l') return 2;
                if (Console::Read(data, 4) != 4 || data[0] != 'l' || data[3] != 't') return 3;
                if (Console::ReadByte() != 10 || Console::ReadByte() != -1) return 4;
                if (Console::TryReadLine(data, 16, ref length) || length != 0) return 5;
                return 0;
            }
            """, System.Text.Encoding.UTF8.GetBytes("Q\0Rtail\nlast\n"), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedReadsRespectTheHostsExistingStdinTextMode(int optimization)
    {
        byte[] expected = OperatingSystem.IsWindows() ? [65, 10, 66] : [65, 13, 10, 66, 26, 67];
        Run($$"""
            int main() {
                char[16] data;
                if (Console::Read(data, 16) != (nuint){{expected.Length}}) return 1;
                {{string.Join("\n", expected.Select((value, index) => $"if ((byte)data[{index}] != (byte){value}) return 2;"))}}
                if (Console::ReadByte() != -1) return 3;
                return 0;
            }
            """, [65, 13, 10, 66, 26, 67], optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OutputOnlyRedirectedStdinReportsIoErrorsInsteadOfEof(int optimization)
    {
        Run("""
            extern void* __gflat_stdio_stdin();
            extern void*? freopen(immutable(char*) path, immutable(char*) mode, void* stream);
            extern void clearerr(void* stream);
            int main() {
                void* stream = __gflat_stdio_stdin();
                if (freopen($PATH, "wb", stream) == null) return 1;
                char data = 'Z';
                try { Console::Read(&data, 1); return 2; }
                catch (std::IO::EndOfStreamException* error) { return 3; }
                catch (std::IO::IOException* error) { }
                if (data != 'Z' || Console::Read(&data, 0) != 0) return 4;
                clearerr(stream);
                try { Console::ReadByte(); return 5; } catch (std::IO::IOException* error) { }
                clearerr(stream);
                try { Console::ReadExactly(&data, 1); return 6; }
                catch (std::IO::EndOfStreamException* error) { return 7; }
                catch (std::IO::IOException* error) { }
                return 0;
            }
            """, [], optimization);
    }
}
