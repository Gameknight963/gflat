using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeFileLocalFunctionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NativeOverloadFamiliesAndTargetTypedPointersRemainFileLocal(int optimization)
    {
        string Native(int value) => $"#lang cpp\nnamespace Native {{ static int Pick(int value) {{ return {value}; }} " +
            $"static int Pick(double value) {{ return {value + 1}; }} }}\n#lang gflat\n";
        await Run([
            new("native-local-overload-first.gf", Native(17) +
                "int First() { int(int)* callback = Native::Pick; return callback(0); }\n" +
                "int FirstDouble() => Native::Pick(0.0);\n" +
                "int main() { if (First() != 17 || Second() != 25 || FirstDouble() != 18 || SecondDouble() != 26) return 1; return 42; }"),
            new("native-local-overload-second.gf", Native(25) +
                "int Second() { int(int)* callback = Native::Pick; return callback(0); }\n" +
                "int SecondDouble() => Native::Pick(0.0);")
        ], optimization);
    }

    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public async Task SameNamedFunctionsBindToTheirOwnTranslationUnitsIncludingFunctionPointers(string language, int optimization)
    {
        string scope = language == "cpp" ? "Native::" : "";
        string Native(int value) => language == "cpp"
            ? $"namespace Native {{ static int LocalValue() {{ return {value}; }} }}"
            : $"static int LocalValue(void) {{ return {value}; }}";
        SourceFile[] sources = [
            new("local-first.gf", $"#lang {language}\n{Native(17)}\n#lang gflat\n" +
                $"int First() {{ int()* callback = {scope}LocalValue; return callback(); }}\n" +
                $"int FirstDirect() => {scope}LocalValue();\n" +
                "int main() { if (First() != 17 || Second() != 25 || FirstDirect() != 17 || SecondDirect() != 25) return 1; return 42; }"),
            new("local-second.gf", $"#lang {language}\n{Native(25)}\n#lang gflat\n" +
                $"int Second() {{ int()* callback = {scope}LocalValue; return callback(); }}\n" +
                $"int SecondDirect() => {scope}LocalValue();")
        ];
        // Repeat the imports to exercise their cached declarations and ownership.
        for (int pass = 0; pass < 2; pass++) await Run(sources, optimization);
    }

    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public async Task IncludedStaticFunctionsKeepSeparateStatePerTranslationUnit(string language, int optimization)
    {
        string directory = Directory.CreateTempSubdirectory("gflat-native-static-header-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "counter.h"), "static int state; static int Next(void) { return ++state; }");
            string prefix = $"#lang {language}\n#include \"counter.h\"\n#lang gflat\n";
            await Run([
                new(Path.Combine(directory, "first.gf"), prefix + "int First() => Next();\n" +
                    "int main() { if (First() != 1 || Second() != 1 || First() != 2 || Second() != 2) return 1; return 42; }"),
                new(Path.Combine(directory, "second.gf"), prefix + "int Second() => Next();")
            ], optimization);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public async Task FileLocalFunctionsShadowProjectVisibleNativeFunctions(string language, int optimization)
    {
        await Run([
            new("native-local-shadow.gf", $"#lang {language}\nstatic int Value(void) {{ return 17; }}\n#lang gflat\nint First() => Value();\n" +
                "int main() { if (First() != 17 || Second() != 25) return 1; return 42; }"),
            new("native-external-shadow.gf", $"#lang {language}\nint Value(void) {{ return 25; }}\n#lang gflat\nint Second() => Value();")
        ], optimization);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void InternalVariadicFunctionsAreNotImportedAsExternalSymbols(string language)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            $"#lang {language}\nstatic int Hidden(int value, ...) {{ return value; }}\n#lang gflat\nint Read() => Hidden(42);",
            options: new() { NoStdLib = true, ForeignCompilerResolver = () => NativeToolchain.FindClang() }));
        Assert.Contains("Unknown function 'Hidden'", error.Description);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void StaticFunctionsAreNotDiscoveredFromOtherSourceFiles(string language)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check([
            new SourceFile("local-definition.gf", $"#lang {language}\nstatic int Hidden(void) {{ return 42; }}\n#lang gflat\n"),
            new SourceFile("local-use.gf", "int Read() => Hidden();")
        ], options: new() { NoStdLib = true, ForeignCompilerResolver = () => NativeToolchain.FindClang() }));
        Assert.Contains("Unknown function 'Hidden'", error.Description);
    }

    private static async Task Run(SourceFile[] sources, int optimization)
    {
        string directory = Directory.CreateTempSubdirectory("gflat-native-static-run-").FullName;
        try
        {
            var program = Compiler.EmitProgram(sources, options: new()
            {
                NoStdLib = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
            });
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization,
                foreignUnits: program.ForeignUnits, nativeLibraries: NativeTestFaultHandling.LinkInputs);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }
}
