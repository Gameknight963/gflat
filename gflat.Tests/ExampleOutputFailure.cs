namespace gflat.Tests;

// Compile the actual tool with a small host-owned stdout failure wrapper.
internal static class ExampleOutputFailure
{
    internal static void AssertReported(string exampleName, int optimization, string diagnostic, params string[] extraArguments)
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
            string wrapper = """
                extern void* __gflat_stdio_stdout();
                extern int setvbuf(void* stream,char* buffer,int mode,nuint count);
                extern int fclose(void* stream);
                extern void*? freopen(readonly(char)* path,readonly(char)* mode,void* stream);
                int main(int argc,char** argv){
                    void* stream=__gflat_stdio_stdout();char[4096] buffer;
                    if(argc==ARGCOUNT+1){
                        if(freopen(argv[1],"rb",stream)==null)return 92;
                        int status=ExampleMain(ARGCOUNT,argv);fclose(stream);return status;
                    }
                    SETUP
                    int status=ExampleMain(argc,argv);
                    // Keep the buffer alive until the stream closes. The tool's
                    // returned status must detect the failure before this cleanup.
                    fclose(stream);return status;
                }
                """.Replace("SETUP", setup).Replace("ARGCOUNT", (2 + extraArguments.Length).ToString());
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("example-with-output-failure.gf", example + failure + wrapper));
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "tool.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string input = Path.Combine(directory, "input.txt");
            File.WriteAllText(input, "needle apple\npear needle\n");
            string[] arguments = [input, .. extraArguments];
            foreach (string[] mode in new[] { arguments, [.. arguments, "read-only-output"] })
            {
                var run = NativeToolchain.Run(executable, mode, 30000);
                Assert.Equal(1, run.ExitCode);
                Assert.Equal("", run.StandardOutput);
                Assert.Equal(diagnostic, run.StandardError.TrimEnd('\r', '\n'));
                Assert.DoesNotContain("Unable to read", run.StandardError);
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
