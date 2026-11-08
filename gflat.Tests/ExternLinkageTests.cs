using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExternLinkageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitCppLinkageCallsNativeNamespaceFunctionsAndTakesTheirAddress(int optimization)
    {
        const string source = """
            namespace Native
            {
                extern "C++" int Answer(int value);
                extern "C++" long Wide(long value);
                extern "C++" int Read(readonly(int)* value) nothrow;
            }
            int main()
            {
                int(int)* answer = Native::Answer;
                int value = 7;
                if (Native::Wide(8000000000l) != 8000000000l) return 1;
                if (Native::Read(&value) != 7) return 2;
                return answer(42);
            }
            """;
        var arguments = NativeRuntime.HostCompilerArguments(true);
        var options = new CompilationOptions { NoStdLib = true, ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = arguments };
        string ir = Compiler.Emit(source, options: options);
        Assert.Contains(OperatingSystem.IsWindows() ? "?Answer@Native@@" : "_ZN6Native6AnswerEi", ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat_cpp_linkage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(input, ir);
            var native = new ForeignTranslationUnit("cpp", "namespace Native { int Answer(int x) { return x; } long long Wide(long long x) { return x; } int Read(const int* x) noexcept { return *x; } }", arguments);
            var compiled = NativeToolchain.Compile(input, executable, optimizationLevel: optimization, foreignUnits: [native]);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ExplicitCppLinkageRejectsUnsupportedValueLayouts()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("struct Value { int X; } extern \"C++\" void Use(Value value);",
            options: new() { NoStdLib = true }));
        Assert.Contains("fixed-width scalar", error.Message);
    }

    [Fact]
    public void BareExternUsesTheLanguageDefinitionSymbolForCallsAndAddresses()
    {
        const string declaration = "namespace Example { extern int Answer(int value); } int main() { int(int)* answer = Example::Answer; return answer(42) + Example::Answer(0); }";
        string ir = Compiler.Emit(declaration, options: new() { NoStdLib = true, NoBuiltins = true });
        string definition = Compiler.Emit("namespace Example { int Answer(int value) => value; }", options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Contains("declare i32 @gflat$Example$Answer(i32)", ir);
        Assert.Contains("@gflat$Example$Answer", definition);
        Assert.DoesNotContain("@Answer(", ir);
    }

    [Fact]
    public void ExplicitCLinkageUsesTheNativeSymbol()
    {
        string ir = Compiler.Emit("extern \"C\" int abs(int value); int main() => abs(-42);",
            options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Contains("declare i32 @abs(i32)", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void VariadicDeclarationsRequireCLinkage()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("extern int print(readonly(char)* format, ...);",
            options: new() { NoStdLib = true, NoBuiltins = true }));
}
