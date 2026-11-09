namespace gflat.Tests;

public sealed class NativeCallbackTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMethodsCanReturnCallbacksWithoutTypedefs(int optimization)
    {
        string code = """
            #lang cpp
            int Twice(int value) { return value * 2; }
            int (*GlobalSafe() noexcept)(int) { return Twice; }
            class OpaqueFactory {
            public:
                int (*Get() const)(int) { return Twice; }
            };
            struct Factory {
                int mode;
                int (*Choose(int (*fallback)(int)) const)(int) { return mode == 1 ? Twice : fallback; }
                int (*Mutable())(int) { ++mode; return Twice; }
                int (*Safe() const noexcept)(int) { return Twice; }
                static int (*Missing())(int) { return nullptr; }
            };
            #lang gflat
            int Triple(int value) => value * 3;
            int main()
            {
                Factory factory = default;
                int(int)*? callback = factory.Choose(Triple);
                if (callback!(14) != 42) return 1;
                callback = factory.Mutable();
                if (factory.mode != 1 || callback!(21) != 42) return 2;
                readonly(Factory)* view = &factory;
                callback = view.Choose(Triple);
                if (callback!(21) != 42) return 3;
                callback = view.Safe();
                if (callback!(21) != 42) return 4;
                if (Factory::Missing() != null) return 5;
                callback = GlobalSafe();
                if (callback!(21) != 42) return 6;
                OpaqueFactory* opaque = new* OpaqueFactory();
                callback = opaque.Get();
                delete opaque;
                if (callback!(21) != 42) return 7;
                return 42;
            }
            """;
        Run(code, optimization);
    }

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
            Callback EchoCallback(Callback callback) { return callback; }
            int IncrementNative(int value) { return value + 1; }
            int (*Choose(int choice, int (*fallback)(int)))(int)
            {
                if (choice == 0) return 0;
                return choice == 1 ? IncrementNative : fallback;
            }
            #lang gflat
            int Sum(int value, void*? context) => value + *(int*)context!;
            int Double(int value) => value * 2;
            int Apply(int(int)*? operation) => operation!(21);
            int main()
            {
                int context = 22;
                if (Invoke(Sum, &context) != 42) return 1;
                if (Nested(Apply, Double) != 42) return 2;
                int(int, void*?)*? echoed = EchoCallback(Sum);
                if (echoed!(20, &context) != 42) return 3;
                int(int)*? chosen = Choose(1, Double);
                if (chosen!(41) != 42) return 4;
                chosen = Choose(2, Double);
                if (chosen!(21) != 42) return 5;
                if (Choose(0, Double) != null) return 6;
                return Optional(null);
            }
            """;
        Run(code, optimization);
    }

    private static void Run(string code, int optimization)
    {
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
