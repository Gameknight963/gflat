namespace gflat.Tests;

// Compile the actual tool with a small host-owned stdout failure wrapper.
internal static class ExampleOutputFailure
{
    internal static void AssertReported(string exampleName, int optimization, string diagnostic, params string[] extraArguments)
        => AssertReportedWithInput(exampleName, optimization, diagnostic, "needle apple\npear needle\n", extraArguments);

    internal static void AssertReportedWithInput(string exampleName, int optimization, string diagnostic,
        string inputText, params string[] extraArguments)
        => AssertReportedCore(exampleName, optimization, diagnostic, inputText, [], extraArguments);

    internal static void AssertReportedWithLeadingArguments(string exampleName, int optimization, string diagnostic,
        params string[] leadingArguments)
        => AssertReportedCore(exampleName, optimization, diagnostic, "needle apple\npear needle\n", leadingArguments, []);

    internal static void AssertReportedWithArguments(string exampleName, int optimization,
        string bufferedFailureDiagnostic, string readOnlyFailureDiagnostic, params string[] arguments)
        => AssertReportedCore(exampleName, optimization, bufferedFailureDiagnostic, "", [], [],
            arguments, readOnlyFailureDiagnostic);

    internal static void AssertReportedWithArguments(string exampleName, int optimization,
        string bufferedFailureDiagnostic, string readOnlyFailureDiagnostic, int expectedExitCode, params string[] arguments)
        => AssertReportedCore(exampleName, optimization, bufferedFailureDiagnostic, "", [], [],
            arguments, readOnlyFailureDiagnostic, expectedExitCode);

    private static void AssertReportedCore(string exampleName, int optimization, string diagnostic,
        string inputText, string[] leadingArguments, string[] extraArguments,
        string[]? exactArguments = null, string? readOnlyDiagnostic = null, int expectedExitCode = 1)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
        string directory = Path.Combine(Path.GetTempPath(), "gflat-output-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", exampleName + ".gf"));
            const string signature = "int main(int argc, char** argv)";
            Assert.Contains(signature, example);
            example = example.Replace(signature, "int ExampleMain(int argc, char** argv)");
            string failure = OperatingSystem.IsWindows() ? """
                extern int _fileno(void* stream);
                extern nint _get_osfhandle(int descriptor);
                extern int CloseHandle(void* handle);
                bool BreakOutput(void* stream){
                    // Keep the CRT descriptor valid to avoid its invalid-parameter
                    // handler; only the underlying OS handle becomes unwritable.
                    return CloseHandle((void*)_get_osfhandle(_fileno(stream)))!=0;
                }
                """ : """
                bool BreakOutput(void* stream)=>freopen("/dev/full","wb",stream)!=null;
                """;
            string setup = OperatingSystem.IsWindows() ? """
                if(setvbuf(stream,buffer,0,4096)!=0)return 90;
                if(!BreakOutput(stream))return 91;
                """ : """
                if(!BreakOutput(stream))return 91;
                if(setvbuf(stream,buffer,0,4096)!=0)return 90;
                """;
            int argumentCount = exactArguments == null
                ? 2 + leadingArguments.Length + extraArguments.Length : 1 + exactArguments.Length;
            int readOnlyPathIndex = exactArguments == null ? 1 + leadingArguments.Length : argumentCount;
            string wrapper = """
                extern void* __gflat_stdio_stdout();
                extern int setvbuf(void* stream,char* buffer,int mode,nuint count);
                extern int fclose(void* stream);
                extern void*? freopen(readonly(char)* path,readonly(char)* mode,void* stream);
                int main(int argc,char** argv){
                    void* stream=__gflat_stdio_stdout();char[4096] buffer;
                    if(argc==ARGCOUNT+1){
                        if(freopen(argv[INPUTINDEX],"rb",stream)==null)return 92;
                        int status=ExampleMain(ARGCOUNT,argv);fclose(stream);return status;
                    }
                    SETUP
                    int status=ExampleMain(argc,argv);
                    // Keep the buffer alive until the stream closes. The tool's
                    // returned status must detect the failure before this cleanup.
                    fclose(stream);return status;
                }
                """.Replace("SETUP", setup)
                .Replace("ARGCOUNT", argumentCount.ToString())
                .Replace("INPUTINDEX", readOnlyPathIndex.ToString());
            string combinedSource = example + failure + wrapper;
            string sourcePath = Path.Combine(directory, "example-with-output-failure.gf");
            File.WriteAllText(sourcePath, combinedSource);
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile(sourcePath, combinedSource));
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "tool.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string input = Path.Combine(directory, "input.txt");
            File.WriteAllText(input, inputText);
            string[] arguments = exactArguments ?? [.. leadingArguments, input, .. extraArguments];
            // Exact-argument tools do not consume an input-file operand. Append
            // a private stdout path solely for wrapper setup, and pass the
            // original argument count to the unchanged example body.
            string[] readOnlyArguments = exactArguments == null
                ? [.. arguments, "read-only-output"] : [.. arguments, input];
            foreach (var (mode, expectedDiagnostic) in new[] {
                (arguments, diagnostic), (readOnlyArguments, readOnlyDiagnostic ?? diagnostic)
            })
            {
                var run = NativeToolchain.Run(executable, mode, 30000);
                Assert.Equal(expectedExitCode, run.ExitCode);
                Assert.Equal("", run.StandardOutput);
                Assert.Equal(expectedDiagnostic, run.StandardError.TrimEnd('\r', '\n'));
                Assert.DoesNotContain("Unable to read", run.StandardError);
            }
        }
        catch (Exception error) { error.Data["ExampleOutputFailureDirectory"] = directory; throw; }
        Directory.Delete(directory, true);
    }
}
