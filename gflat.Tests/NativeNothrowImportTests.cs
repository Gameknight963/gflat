using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class NativeNothrowImportTests
{
    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void ExplicitNativeContractsAvoidUnwindBoundariesForHostAndVariadicCalls(string language, int optimization)
    {
        string native = language == "cpp" ? """
            int Read(int value) noexcept { return value; }
            int Explicit(int value) noexcept(true) { return value; }
            extern "C" int Variadic(int value, ...) noexcept { return value; }
            """ : """
            __attribute__((nothrow)) int Read(int value) { return value; }
            __attribute__((nothrow)) int Explicit(int value) { return value; }
            __attribute__((nothrow)) int Variadic(int value, ...) { return value; }
            """;
        Run("#lang " + language + "\n" + native + "\n#lang gflat\n" + """
            int Host() nothrow => Read(10) + Explicit(10) + Variadic(22, 1, 2);
            int main() nothrow => Host();
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NothrowMethodsRecordsReferencesAndCallbackResultsKeepTheirContracts(int optimization)
        => Run("""
            #lang cpp
            int Twice(int value) { return value * 2; }
            struct Pair { int left; int right; };
            Pair Make() noexcept { return Pair{19, 22}; }
            void Increment(int& value) noexcept { ++value; }
            struct Reader {
                int value;
                int Read() const noexcept { return value; }
                __attribute__((nothrow)) int Annotated() const { return value; }
                auto Callback() const noexcept -> int (*)(int) { return Twice; }
            };
            #lang gflat
            Pair MakeHost() nothrow => Make();
            int main() nothrow
            {
                Pair pair = MakeHost();
                Increment(ref pair.right);
                Reader reader = default;
                reader.value = 42;
                readonly(Reader)* view = &reader;
                if (view.Read() != 42 || view.Annotated() != 42) return 1;
                int(int)*? callback = view.Callback();
                if (callback == null) return 2;
                return pair.left + pair.right;
            }
            """, optimization);

    [Theory]
    [InlineData("cpp", "int Read() { return 42; }")]
    [InlineData("cpp", "int Read() noexcept(false) { return 42; }")]
    [InlineData("cpp", "int Read() noexcept(sizeof(\"__attribute__((nothrow))\") == 0) { return 42; }")]
    [InlineData("c", "__attribute__((pure)) int Read() { return 42; }")]
    public void UnknownAndPotentiallyThrowingNativeFunctionsRemainThrowing(string language, string declaration)
    {
        var program = Compiler.EmitProgram([new SourceFile("unknown-nothrow.gf", "#lang " + language + "\n" +
            declaration + "\n#lang gflat\nint Host() nothrow => Read(); int main() => Host();")], options: Options());
        Assert.Contains("invoke", program.Ir);
        Assert.Contains("personality", program.Ir);
    }

    [Fact]
    public void NativeAllocationRemainsThrowingWhenTheConstructorIsNothrow()
    {
        var program = Compiler.EmitProgram([new SourceFile("nothrow-constructor.gf", """
            #lang cpp
            class Owner { public: Owner() noexcept {} };
            #lang gflat
            Owner* Make() nothrow => new* Owner();
            int main() { Owner* value = Make(); delete value; return 0; }
            """)], options: Options());
        string body = Regex.Match(program.Ir,
            @"(?ms)^define [^\n]*@gflat\$Make\([^\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);
        Assert.Contains("invoke", body);
    }

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        InternalizeFunctions = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    private static void Run(string code, int optimization)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-nothrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("native-nothrow.gf", code)], options: Options());
            Assert.DoesNotContain("invoke", program.Ir);
            Assert.DoesNotContain("personality", program.Ir);
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
