namespace gflat.Tests;

public sealed class NativeTrailingReturnTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TrailingReturnsPreserveScalarsPointersRecordsAndCallbacks(int optimization)
    {
        const string code = """
            #lang cpp
            namespace Native {
                struct Pair { int left; int right; };
                auto Make(int left, int right) -> Pair { return Pair{left, right}; }
                auto Sum(const Pair* value) -> int { return value->left + value->right; }
                auto Text() -> const char* { return "ok"; }
                auto Twice(int value) -> int { return value * 2; }
                auto Echo(int (*callback)(int)) noexcept -> int (*)(int) { return callback; }
                struct Reader {
                    int increment;
                    auto Read(int value) const noexcept -> int { return increment + value; }
                    auto Callback() const -> int (*)(int) { return Twice; }
                    static auto Missing() -> void* { return nullptr; }
                };
                auto Safe() noexcept(noexcept(((Reader*)nullptr)->Read(0))) -> int { return 42; }
            }
            #lang gflat
            int Triple(int value) => value * 3;
            int main()
            {
                Native::Pair pair = Native::Make(19, 23);
                if (Native::Sum(&pair) != 42) return 1;
                readonly(char)* text = Native::Text()!;
                if (text[0] != 'o' || text[1] != 'k' || text[2] != 0) return 2;
                int(int)*? callback = Native::Echo(Triple);
                if (callback!(14) != 42) return 3;
                Native::Reader reader = default;
                reader.increment = 40;
                readonly(Native::Reader)* view = &reader;
                if (view.Read(2) != 42) return 4;
                callback = view.Callback();
                if (callback!(21) != 42) return 5;
                if (Native::Reader::Missing() != null) return 6;
                if (Native::Safe() != 42) return 7;
                return 42;
            }
            """;
        string root = Path.Combine(Path.GetTempPath(), "gflat-trailing-return-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new CompilationOptions { NoStdLib = true, InternalizeFunctions = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(), ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true) };
            var program = Compiler.EmitProgram([new SourceFile("trailing-return.gf", code)], options: options);
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
