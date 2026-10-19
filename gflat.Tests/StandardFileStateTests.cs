using System.Diagnostics;

namespace gflat.Tests;

public sealed class StandardFileStateTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Compile(string directory, string program)
    {
        var files = new[] { "core/IOException.gf", "core/EndOfStreamException.gf", "libc/File.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files));
        var compiled = NativeToolchain.Compile(ir, exe);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        return exe;
    }

    [Fact]
    public void PeekDoesNotConsumeAndEofIsObservedRatherThanPredicted()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "bytes.bin");
            File.WriteAllBytes(path, [65, 0, 255]);
            string exe = Compile(directory, $$"""
                using std;
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    if (input.EndOfFile || input.PeekByte() != 65 || input.PeekByte() != 65 || input.EndOfFile) return 1;
                    if (input.ReadByte() != 65 || input.PeekByte() != 0 || input.ReadByte() != 0 || input.ReadByte() != 255) return 2;
                    if (input.EndOfFile) return 3;
                    if (input.PeekByte() != -1 || !input.EndOfFile) return 4;
                    input.ClearError();
                    if (input.EndOfFile) return 5;
                    if (input.ReadByte() != -1 || !input.EndOfFile) return 6;
                    input.ClearError(); char[1] buffer;
                    if (input.Read(buffer, 0) != (nuint)0 || input.EndOfFile) return 7;
                    if (input.Read(buffer, 1) != (nuint)0 || !input.EndOfFile) return 8;
                    input.Close();
                    try { bool ended = input.EndOfFile; return 9; } catch (IOException* error) { }
                    try { input.PeekByte(); return 10; } catch (IOException* error) { }
                    try { input.ClearError(); return 11; } catch (IOException* error) { }
                    return 0;
                }
                """);
            var result = NativeToolchain.Run(exe, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TemporaryFilesAreRemovedAtExplicitCloseAndExceptionUnwinding()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-temp-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Process? process = null;
        try
        {
            string scratch = Path.Combine(directory, "scratch");
            Directory.CreateDirectory(scratch);
            string exe = Compile(directory, """
                using std;
                extern int puts(readonly(char*) text);
                extern int fflush(void*? stream);
                extern int getchar();
                void Wait(readonly(char*) stage) { puts(stage); fflush(null); getchar(); }
                int main() {
                    {
                        File original = File::OpenTemporary();
                        File temporary = move original;
                        if (!temporary.IsOpen) return 1;
                        temporary.Write("temporary bytes"); temporary.Flush();
                        Wait("open");
                        temporary.Close();
                        if (temporary.IsOpen) return 2;
                        Wait("closed");
                    }
                    try {
                        File temporary = File::OpenTemporary();
                        temporary.Write("scope cleanup"); temporary.Flush();
                        Wait("scope");
                        throw new Exception("leave scope");
                    } catch (Exception* error) { }
                    Wait("unwound");
                    for (int i = 0; i < 600; i++) { File temporary = File::OpenTemporary(); temporary.WriteByte('x'); }
                    return 0;
                }
                """);
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment["TMP"] = scratch;
            start.Environment["TEMP"] = scratch;
            start.Environment["TMPDIR"] = scratch;
            process = Process.Start(start)!;
            foreach (string stage in new[] { "open", "closed", "scope", "unwound" })
            {
                string? actual = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(stage, actual);
                // Unix runtimes may unlink immediately, so no path is observable.
                // Modern UCRT uses the child's temp directory until close.
                if (OperatingSystem.IsWindows())
                    Assert.Equal(stage is "open" or "scope" ? 1 : 0, Directory.GetFiles(scratch).Length);
                await process.StandardInput.WriteLineAsync();
                await process.StandardInput.FlushAsync();
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally
        {
            if (process != null)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
                process.Dispose();
            }
            Directory.Delete(directory, true);
        }
    }
}
