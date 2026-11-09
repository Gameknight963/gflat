namespace gflat.Tests;

public sealed class NativeBuiltinHeaderTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void ExplicitCHeaderDeclarationsRemainVisibleAfterImplicitBuiltins(int optimization, bool noStdLib)
    {
        const string code = """
            #lang c
            #include <string.h>
            int NativeSizeBytes(void) { return (int)sizeof(size_t); }
            #lang gflat
            int main()
            {
                char[2] source = ['*', '\0'];
                char[2] output;
                memcpy(&output[0], &source[0], 2);
                var length = strlen(&output[0]);
                if (memcmp(&output[0], &source[0], 2) != 0 || (nuint)length != (nuint)1) return 1;
                if (sizeof([:typeof(length):]) != NativeSizeBytes()) return 2;
                return (int)output[0];
            }
            """;
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-builtins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new CompilationOptions
            {
                NoStdLib = noStdLib,
                InternalizeFunctions = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
            };
            var program = Compiler.EmitProgram([new SourceFile("native-builtins.gf", code)], options: options);
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native builtin header exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
