using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public class ConsoleTests
{
    private static SourceFile[] Sources(string program, bool strings = false)
    {
        string[] names = strings
            ? ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf", "libc/Console/Console.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "libc/Console/ConsoleString.gf"]
            : ["core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf", "libc/Console/Console.gf"];
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
    }

    private static ExecutionResult Run(string source, string input = "", bool strings = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-console-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "console.ll"), output = Path.Combine(directory, "console.exe");
            File.WriteAllText(ir, Compiler.Emit(Sources(source, strings)));
            var compiled = NativeToolchain.Compile(ir, output);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            using var process = new Process { StartInfo = new ProcessStartInfo(output) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false)
            } };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Console test timed out"); }
            return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CStringAndCountedWritesDoNotAddUnexpectedNewlinesOrAllocate()
    {
        var result = Run("""
            namespace Memory {
                public replace void*? Allocate(nuint size) => null;
                public replace void Free(void* value) nothrow { }
            }
            int main() {
                Console::Write("A"); Console::Write("B\0ignored");
                Console::Write("C\0D", 3); Console::WriteLine("E");
                Console::WriteLine(); Console::WriteLine("F\0G", 3);
                Console::Flush(); return 42;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("ABC\0DE\n\nF\0G\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("first\n\nlast")]
    [InlineData("first\r\n\r\nlast\r")]
    [InlineData("a\0b\n")]
    [InlineData("�\n")]
    public void RawBufferReadsPreserveBytesAndDistinguishEmptyLinesFromEof(string input)
    {
        var result = Run("""
            namespace Memory {
                public replace void*? Allocate(nuint size) => null;
                public replace void Free(void* value) nothrow { }
            }
            int main() {
                char[1024] buffer;
                nuint length = 0;
                while (Console::TryReadLine(buffer, 1024, ref length)) {
                    if (buffer[length] != '\0') return 1;
                    Console::Write(buffer, length); Console::Write("|");
                }
                if (length != (nuint)0 || buffer[0] != '\0') return 2;
                return 42;
            }
            """, input);
        Assert.Equal(42, result.ExitCode);
        string normalized = input.Replace("\r\n", "\n");
        string expected = string.Join("|", normalized.Split('\n'));
        if (normalized.Length != 0 && !normalized.EndsWith('\n')) expected += "|";
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void OverflowConsumesExactlyOneLineAndLeavesATerminatedPrefix()
    {
        var result = Run("""
            int main() {
                char[4] buffer; nuint length = 0;
                try { Console::TryReadLine(buffer, 4, ref length); return 1; }
                catch (ref std::BufferTooSmallException error) { }
                if (length != (nuint)3 || buffer[0] != 'a' || buffer[2] != 'c' || buffer[3] != '\0') return 2;
                if (Console::ReadLine(buffer, 4) != (nuint)2 || buffer[0] != 'o' || buffer[1] != 'k') return 3;
                try { Console::ReadLine(buffer, 4); return 4; }
                catch (ref std::IO::EndOfStreamException error) { return 42; }
            }
            """, "abcdef\nok\n");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ZeroCapacityThrowsTypedErrorWithoutTouchingStorageOrConsumingInput()
    {
        var result = Run("""
            int main() {
                char sentinel = '?'; nuint length = 99;
                try { Console::TryReadLine(&sentinel, 0, ref length); return 1; }
                catch (ref std::BufferTooSmallException error) { }
                if (sentinel != '?' || length != (nuint)0) return 2;
                char[8] buffer;
                if (Console::ReadLine(buffer, 8) != (nuint)3 || buffer[0] != 'a' || buffer[2] != 'c') return 3;
                return 42;
            }
            """, "abc\n");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void StringAllocationFailurePreservesValueAndAllowsReadingRemainingInput()
    {
        var result = Run("""
            using std; using std::Text; using std::IO;
            extern void*? malloc(nuint size);
            extern void free(void* data);
            static class State { public static bool Fail; }
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    if (State::Fail && size == (nuint)257) return null;
                    return malloc(size);
                }
                public replace void Free(void* data) nothrow { free(data); }
            }
            int main() {
                String line = new String();
                State::Fail = true;
                try { Console::TryReadLine(ref line); return 1; } catch (ref BadAllocException error) { }
                if (line.Length != (nuint)0 || line.Data[0] != '\0') return 2;
                State::Fail = false;
                if (!Console::TryReadLine(ref line) || line.Length != (nuint)44) return 3;
                if (line.Data[0] != 'x' || line.Data[43] != 'x' || line.Data[44] != '\0') return 4;
                String next = Console::ReadLine();
                if (next.Length != (nuint)4 || next.Data[0] != 'n' || next.Data[3] != 't') return 5;
                return 42;
            }
            """, new string('x', 300) + "\nnext\n", strings: true);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CapacityIncludesTerminatorAndCrLfDoesNotOverflowExactFit()
    {
        Assert.Equal(42, Run("""
            int main() {
                char[1] empty;
                if (Console::ReadLine(empty, 1) != (nuint)0 || empty[0] != '\0') return 1;
                char[4] buffer;
                if (Console::ReadLine(buffer, 4) != (nuint)3 || buffer[3] != '\0') return 2;
                try { Console::ReadLine(buffer, 0); return 3; }
                catch (ref Exception error) { return 42; }
            }
            """, "\nabc\r\n").ExitCode);
    }

    [Fact]
    public void StringOverloadsBorrowOutputAndReuseReadCapacity()
    {
        var result = Run("""
            using std; using std::Text; using std::IO;
            int main() {
                String text = s"A\0B";
                Console::Write(ref text); Console::WriteLine(ref text);
                String line = Console::ReadLine();
                readonly(char)* original = line.Data;
                nuint capacity = line.Capacity;
                if (!Console::TryReadLine(ref line)) return 1;
                if (line.Data != original || line.Capacity != capacity || line.Length != (nuint)2) return 2;
                Console::WriteLine(ref line);
                if (Console::TryReadLine(ref line) || line.Length != (nuint)0) return 3;
                try { Console::ReadLine(); return 4; }
                catch (ref EndOfStreamException error) { return 42; }
            }
            """, "a longer line\nok\n", true);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("A\0BA\0B\nok\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadAndWriteFailuresAreIoExceptions()
    {
        string file = Path.GetTempFileName();
        try
        {
            string literal = "\"" + file.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var result = Run("""
                extern void* __gflat_stdio_stdin();
                extern void* __gflat_stdio_stdout();
                extern void*? freopen(immutable(char*) path, immutable(char*) mode, void* stream);
                int main() {
                """ + $$"""
                    if (freopen({{literal}}, "w", __gflat_stdio_stdin()) == null) return 1;
                    char[8] buffer; nuint length = 0;
                    try { Console::TryReadLine(buffer, 8, ref length); return 2; }
                    catch (ref std::IO::IOException error) { }
                    if (freopen({{literal}}, "r", __gflat_stdio_stdout()) == null) return 3;
                    try { Console::Write("text"); return 4; }
                    catch (ref std::IO::IOException error) { }
                    return 42;
                }
                """);
            Assert.Equal(42, result.ExitCode);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CliRunShowsPromptBeforeWaitingForInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-console-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sources = Sources("""
                int main() {
                    Console::WriteLine("prompt"); Console::Flush();
                    char[8] buffer;
                    nuint count = Console::ReadLine(buffer, 8);
                    Console::WriteLine(buffer, count); return 42;
                }
                """);
            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach (var source in sources)
            {
                string file = Path.Combine(root, source.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, source.Text);
                start.ArgumentList.Add(file);
            }
            start.ArgumentList.Add("--run"); start.ArgumentList.Add("-o"); start.ArgumentList.Add(Path.Combine(root, "main.exe"));
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                string? prompt = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal("prompt", prompt);
                await process.StandardInput.WriteLineAsync("hello"); process.StandardInput.Close();
                var rest = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(42, process.ExitCode);
                Assert.Equal("hello", (await rest).TrimEnd('\r', '\n'));
                Assert.Equal("", await errors);
            }
            finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StringReaderHandlesMultipleChunksAndTrailingCrAtEof()
    {
        string input = new string('x', 255) + "\r";
        var result = Run("""
            using std; using std::Text; using std::IO;
            int main() {
                String line = Console::ReadLine();
                if (line.Length != (nuint)256 || line[(nuint)255] != '\r') return 1;
                Console::Write(ref line); return 42;
            }
            """, input, true);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal(input, result.StandardOutput);
        string longLine = new('x', 1200);
        var large = Run("""
            using std; using std::Text; using std::IO;
            int main() { String line = Console::ReadLine(); Console::Write(ref line); return 42; }
            """, longLine + "\n", true);
        Assert.Equal(42, large.ExitCode);
        Assert.Equal(longLine, large.StandardOutput);
    }
}
