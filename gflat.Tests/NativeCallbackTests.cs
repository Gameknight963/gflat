namespace gflat.Tests;

public sealed class NativeCallbackTests
{
    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void NativeCallbacksPassPointersAndSupportNestedSignatures(string language, int optimization)
    {
        string code = "#lang " + language + "\n" + """
            typedef int (*Callback)(int, void*);
            int Invoke(Callback callback, void* context) { return callback(20, context); }
            int Nested(int (*callback)(int (*)(int)), int (*argument)(int)) { return callback(argument); }
            int Optional(Callback callback) { return callback == 0 ? 42 : 0; }
            #lang gflat
            int Sum(int value, void*? context) => value + *(int*)context!;
            int Double(int value) => value * 2;
            int Apply(int(int)*? operation) => operation!(21);
            int main()
            {
                int context = 22;
                if (Invoke(Sum, &context) != 42) return 1;
                if (Nested(Apply, Double) != 42) return 2;
                return Optional(null);
            }
            """;
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new CompilationOptions { NoStdLib = true, InternalizeFunctions = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(), ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true) };
            var program = Compiler.EmitProgram([new SourceFile("callbacks.gf", code)], options: options);
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
