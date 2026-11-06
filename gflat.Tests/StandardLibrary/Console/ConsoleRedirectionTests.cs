using System.Diagnostics;
using gflat.CompileExceptions;

namespace gflat.Tests.Std.Console;

public sealed class ConsoleRedirectionTests
{
    private const string AllocationProbe = """
        int allocationAttempts = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) { allocationAttempts++; return null; }
            public replace void Free(void* storage) nothrow {}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RedirectedChildPipesAreObservedWithoutConsumingInputWritingOrAllocating(int optimization)
    {
        string directory = TemporaryDirectory();
        try
        {
            string executable = Compile(directory, AllocationProbe + """
                extern "C" void* __gflat_stdio_stdin() nothrow;
                extern "C" int fgetc(void* stream) nothrow;
                int main() nothrow {
                    for (int index = 0; index < 32; index++) {
                        if (!Console::IsInputRedirected || !Console::IsOutputRedirected || !Console::IsErrorRedirected) return 1;
                    }
                    if (fgetc(__gflat_stdio_stdin()) != 81 || fgetc(__gflat_stdio_stdin()) != -1) return 2;
                    if (allocationAttempts != 0) return 3;
                    return 0;
                }
                """, optimization);
            var result = await Run(executable, [], "Q");
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StandardOutput);
            Assert.Empty(result.StandardError);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CurrentCrtFilesAndNullDevicesRemainRedirectedWithoutQueryIo(int optimization)
    {
        string directory = TemporaryDirectory();
        try
        {
            string input = Path.Combine(directory, "input.txt");
            string output = Path.Combine(directory, "output.txt");
            string error = Path.Combine(directory, "error.txt");
            File.WriteAllText(input, "Q");
            string executable = Compile(directory, AllocationProbe + """
                extern "C" void* __gflat_stdio_stdin() nothrow;
                extern "C" void* __gflat_stdio_stdout() nothrow;
                extern "C" void* __gflat_stdio_stderr() nothrow;
                extern "C" void*? freopen(readonly(char)* path, readonly(char)* mode, void* stream) nothrow;
                extern "C" int fgetc(void* stream) nothrow;
                int main(int argc, char** argv) nothrow {
                    if (argc != 5) return 1;
                    if (!Console::IsInputRedirected || !Console::IsOutputRedirected || !Console::IsErrorRedirected) return 2;
                    if (freopen(argv[1], "rb", __gflat_stdio_stdin()) == null) return 3;
                    if (freopen(argv[2], "wb", __gflat_stdio_stdout()) == null) return 4;
                    if (freopen(argv[3], "wb", __gflat_stdio_stderr()) == null) return 5;
                    for (int index = 0; index < 32; index++) {
                        if (!Console::IsInputRedirected || !Console::IsOutputRedirected || !Console::IsErrorRedirected) return 6;
                    }
                    if (argv[4][0] == '1' && fgetc(__gflat_stdio_stdin()) != 81) return 7;
                    if (fgetc(__gflat_stdio_stdin()) != -1) return 8;
                    if (allocationAttempts != 0) return 9;
                    return 0;
                }
                """, optimization);
            var files = await Run(executable, [input, output, error, "1"], "");
            Assert.Equal(0, files.ExitCode);
            Assert.Empty(files.StandardOutput);
            Assert.Empty(files.StandardError);
            Assert.Equal("Q", File.ReadAllText(input));
            Assert.Empty(File.ReadAllBytes(output));
            Assert.Empty(File.ReadAllBytes(error));

            string device = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            var devices = await Run(executable, [device, device, device, "0"], "");
            Assert.Equal(0, devices.ExitCode);
            Assert.Empty(devices.StandardOutput);
            Assert.Empty(devices.StandardError);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BareModuleAndSelectorBridgeUseOnlyTheCNativeProfile(int optimization)
    {
        string directory = TemporaryDirectory();
        try
        {
            string ir = Compiler.Emit(Sources("""
                extern "C" int __gflat_stdio_is_redirected(int selector) nothrow;
                int main() nothrow {
                    if (__gflat_stdio_is_redirected(-1) != 1 || __gflat_stdio_is_redirected(3) != 1
                        || __gflat_stdio_is_redirected(2147483647) != 1) return 1;
                    if (!Console::IsInputRedirected || !Console::IsOutputRedirected || !Console::IsErrorRedirected) return 2;
                    return 0;
                }
                """), options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
            Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(ir));
            Assert.DoesNotContain("invoke ", ir);
            Assert.Contains("nounwind", ir);
            string executable = CompileIr(directory, ir, optimization);
            var result = await Run(executable, [], "");
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StandardOutput);
            Assert.Empty(result.StandardError);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData("IsInputRedirected")]
    [InlineData("IsOutputRedirected")]
    [InlineData("IsErrorRedirected")]
    public void RedirectionPropertiesHaveNoSetters(string property)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources($$"""
            int main() { Console::{{property}} = false; return 0; }
            """)));
        Assert.Contains("setter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new SourceFile[] {
            new("libc/Console/redirection.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/libc/Console/redirection.gf"))),
            new("console-redirection.gf", code)
        };

    private static string TemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat-console-redirection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Compile(string directory, string code, int optimization)
        => CompileIr(directory, Compiler.Emit(Sources(code), options: new() { InternalizeFunctions = true }), optimization);

    private static string CompileIr(string directory, string ir, int optimization)
    {
        string input = Path.Combine(directory, "program.ll"), output = Path.Combine(directory, "program.exe");
        File.WriteAllText(input, ir);
        var result = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return output;
    }

    private static async Task<ExecutionResult> Run(string executable, string[] arguments, string input)
    {
        using var process = new Process {
            StartInfo = new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool started = false;
        try
        {
            process.Start();
            started = true;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
            return new ExecutionResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }
        }
    }
}
