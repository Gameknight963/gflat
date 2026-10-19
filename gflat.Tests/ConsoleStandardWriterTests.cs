using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConsoleStandardWriterTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static SourceFile[] Sources(string program) => new[] {
        "core/IOException.gf", "core/EndOfStreamException.gf", "core/BufferTooSmallException.gf", "core/Formatting.gf",
        "libc/Console/Console.gf", "libc/Console/StandardWriters.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
        .Append(new SourceFile("main.gf", program)).ToArray();

    [Fact]
    public void StandardWritersSeparateStreamsFormatDirectlyAndNeverAllocate()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint count) { Calls::Allocations++; return null; }
                public replace void Free(void* storage) { }
            }
            class Label : IFormattable {
                public Label() { }
                public readonly void Format(ITextWriter* output) { output.Write("<custom>", 8); }
            }
            int main() {
                if (Calls::Allocations != 0) return 1;
                ITextWriter* output = Console::Out;
                ITextWriter* error = Console::Error;
                output.Write("", 0); error.Write("", 0);
                output.Write("A\0B", 3);
                int number = 42; number.Format(Console::Out);
                true.Format(Console::Error); error.Write("\0!", 2);
                Label label = new Label();
                IFormattable* custom = &label; custom.Format(Console::Error);
                Console::Flush(); Console::FlushError();
                if (Calls::Allocations != 0) return 2;
                return 0;
            }
            """));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        Assert.Equal("A\0B42", result.StandardOutput);
        Assert.Equal("true\0!<custom>", result.StandardError);
    }

    [Theory]
    [InlineData("Out", "Error")]
    [InlineData("Error", "Out")]
    public void StandardWriterPropertiesHaveNoSetters(string property, string other)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources($$"""
            int main() { Console::{{property}} = Console::{{other}}; return 0; }
            """)));
        Assert.Contains("setter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BorrowingTheGlobalWritersDoesNotIntroduceUnwindChecks()
    {
        string ir = Compiler.Emit(Sources("""
            ITextWriter* BorrowOut() nothrow => Console::Out;
            ITextWriter* BorrowError() nothrow => Console::Error;
            int main() { ITextWriter* output = BorrowOut(); ITextWriter* error = BorrowError(); return 0; }
            """));
        foreach (string name in new[] { "BorrowOut", "BorrowError" })
        {
            int start = ir.IndexOf("@gflat$" + name + "()", StringComparison.Ordinal);
            Assert.True(start >= 0, name);
            int end = ir.IndexOf("\n}", start, StringComparison.Ordinal);
            Assert.True(end > start, name);
            string function = ir[start..end];
            Assert.Contains("nounwind", function);
            Assert.DoesNotContain("invoke", function);
            Assert.DoesNotContain("__gflat_eh", function);
        }
    }

    [Fact]
    public void CapturedWritersFollowStandardStreamRedirectionAndReportErrors()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-standard-writers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "read-only.txt");
            File.WriteAllText(path, "kept");
            var result = CompilerTestHelper.Run(Sources($$"""
                extern void* __gflat_stdio_stdout();
                extern void* __gflat_stdio_stderr();
                extern void*? freopen(readonly(char*) path, readonly(char*) mode, void* stream);
                int main() {
                    ITextWriter* output = Console::Out;
                    ITextWriter* error = Console::Error;
                    if (freopen({{Literal(path)}}, "rb", __gflat_stdio_stdout()) == null) return 1;
                    output.Write("", 0);
                    try { output.Write("x", 1); return 2; } catch (std::IOException* failure) { }
                    if (freopen({{Literal(path)}}, "rb", __gflat_stdio_stderr()) == null) return 3;
                    error.Write("", 0);
                    try { error.Write("x", 1); return 4; } catch (std::IOException* failure) { }
                    return 0;
                }
                """));
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}");
            Assert.Equal("kept", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void LinuxBufferedErrorFlushReportsDelayedWriteFailure()
    {
        if (!OperatingSystem.IsLinux()) return;
        var result = CompilerTestHelper.Run(Sources("""
            extern void* __gflat_stdio_stderr();
            extern void*? freopen(readonly(char*) path, readonly(char*) mode, void* stream);
            extern int setvbuf(void* stream, char* buffer, int mode, nuint size);
            extern int fclose(void* stream);
            int main() {
                void* stream = __gflat_stdio_stderr();
                if (freopen("/dev/full", "wb", stream) == null) return 1;
                char[128] buffer;
                // _IOFBF is 0 on the Linux libc targets exercised by CI.
                if (setvbuf(stream, buffer, 0, 128) != 0) return 2;
                Console::Error.Write("pending bytes", 13);
                try { Console::FlushError(); fclose(stream); return 3; }
                catch (std::IOException* failure) { }
                fclose(stream);
                return 0;
            }
            """));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
