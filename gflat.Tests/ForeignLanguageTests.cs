using gflat.CompileExceptions;
using gflat.Preprocessing;

namespace gflat.Tests;

public sealed class ForeignLanguageTests
{
    [Theory]
    [InlineData("<input>")]
    [InlineData("source & name.gf")]
    [InlineData("source + name.gf")]
    [InlineData("source's name.gf")]
    [InlineData("source \u00e9.gf")]
    [InlineData("<quoted\"input>")]
    public void SourceNamesRemainValidNativeLineFilenames(string path)
    {
        foreach (string language in new[] { "c", "cpp" })
        {
            string code = "#lang " + language + "\nint Answer(void) { return 42; }\n" +
                "#lang gflat\nint Read() => Answer();";
            var (ast, _) = Compiler.Check([new SourceFile(path, code)], options: new() { NoStdLib = true });
            Assert.Single(ast.ForeignUnits);
        }
    }

    [Theory]
    [InlineData("<input>")]
    [InlineData("source & name.gf")]
    [InlineData("source + name.gf")]
    [InlineData("source's name.gf")]
    [InlineData("source \u00e9.gf")]
    [InlineData("<quoted\"input>")]
    public void NativeDiagnosticsPreserveSourceNamesAndLines(string path)
    {
        foreach (string language in new[] { "c", "cpp" })
        {
            string code = "#lang " + language + "\nint Answer(void) { return missing_name; }\n#lang gflat\n";
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check([new SourceFile(path, code)], options: new() { NoStdLib = true }));
            Assert.True(error.Description.Contains(path + ":2:", StringComparison.Ordinal), error.Description);
            Assert.Contains("missing_name", error.Description);
        }
    }

    [Fact]
    public void SwitchesPreserveForeignSourceAndGflatLocations()
    {
        const string code = "#define NUMBER 3\n#lang cpp\n#define NUMBER 7\nint native() { return NUMBER; }\n#lang gflat\nint answer = NUMBER;";
        var result = new Preprocessor().Process(new SourceFile("switch.gf", code));
        var foreign = Assert.Single(result.ForeignSources);
        Assert.Equal("cpp", foreign.Language);
        Assert.Contains("#define NUMBER 7\nint native()", foreign.Span.Source!.Text.Substring(foreign.Span.Start, foreign.Span.Length));
        Assert.DoesNotContain(result.Tokens, token => token.Text == "native");
        Assert.Equal(6, Assert.Single(result.Tokens, token => token.Text == "answer").Line);
        Assert.Contains(result.Tokens, token => token.Text == "3");
    }

    [Fact]
    public void ForeignModeEndsAtEofAndDoesNotLeakToAnotherRoot()
    {
        var processor = new Preprocessor();
        Assert.Single(processor.Process(new SourceFile("one.gf", "#lang cpp\nint x;" )).ForeignSources);
        var second = processor.Process(new SourceFile("two.gf", "int y;"));
        Assert.Empty(second.ForeignSources);
        Assert.Contains(second.Tokens, token => token.Text == "y");
    }

    [Fact]
    public void RawStringsCommentsAndContinuedMacrosDoNotSwitchLanguages()
    {
        const string code = """"
            #lang cpp
            const char* text = R"marker(
            #lang gflat
            )marker";
            /*
            #lang gflat
            */
            #define TEXT \
            #lang gflat
            int value = 1'000;
            #lang gflat
            int main() { return 0; }
            """";
        var result = new Preprocessor().Process(new SourceFile("raw.gf", code));
        Assert.Single(result.ForeignSources);
        Assert.Contains(result.Tokens, token => token.Text == "main");
        Assert.DoesNotContain(result.Tokens, token => token.Text == "value");
    }

    [Fact]
    public void InactiveGflatSwitchesAreIgnored()
    {
        const string code = "#if 0\n#lang cpp\n#endif\nint main() { return 0; }";
        var result = new Preprocessor().Process(new SourceFile("inactive.gf", code));
        Assert.Empty(result.ForeignSources);
        Assert.Contains(result.Tokens, token => token.Text == "main");
    }

    [Theory]
    [InlineData("#lang rust", "Expected #lang")]
    [InlineData("#lang", "Expected #lang")]
    [InlineData("#lang cpp extra", "Expected #lang")]
    [InlineData("void f() {\n#lang cpp\n", "file scope")]
    public void InvalidSwitchesHaveSourceDiagnostics(string code, string message)
        => Assert.Contains(message, Assert.Throws<TypeCheckException>(() => new Preprocessor().Process(new SourceFile("invalid.gf", code))).Description);

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void CAndCppDefinitionsHeadersNamespacesAndOverloadsRun(int optimization, bool internalize)
    {
        const string code = """
            #lang c
            #include <stdlib.h>
            #include <stdio.h>
            static int from_c(int x) { return x + 1; }
            #lang cpp
            #include <string>
            namespace Native {
                int twice(int x) { return x * 2; }
                double twice(double x) { return x * 2; }
                int text_size() { return (int)std::string("hello").size(); }
            }
            namespace A { using Count = short; Count identity(Count x) { return x; } }
            namespace B { using Count = long long; Count identity(Count x) { return x; } }
            #lang gflat
            int main()
            {
                if (from_c(4) != 5) return 1;
                if (Native::twice(6) != 12) return 2;
                if (Native::twice(1.5) != 3.0) return 3;
                if (Native::text_size() != 5) return 4;
                if (abs(-9) != 9) return 5;
                if (A::identity((short)7) != 7) return 6;
                if (B::identity(8000000000l) != 8000000000l) return 7;
                if (printf("%.*s", 0, "ignored") != 0) return 8;
                return 0;
            }
            """;
        string directory = Path.Combine(Path.GetTempPath(), "gflat_foreign_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new CompilationOptions { NoStdLib = true, InternalizeFunctions = internalize,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(), ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true) };
            var program = Compiler.EmitProgram([new SourceFile("foreign.gf", code)], options: options);
            Assert.Equal(2, program.ForeignUnits.Count);
            string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, exe, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(exe, []).ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TextuallyIncludedHostHeadersCanSwitchTheFollowingSourceLanguage()
    {
        string root = Path.GetFullPath("foreign-includes/main.gf");
        var header = new SourceFile(Path.Combine(Path.GetDirectoryName(root)!, "switch.gfh"), "#lang cpp\nint from_header();\n");
        var source = new SourceFile(root, "#include \"switch.gfh\"\nint still_cpp();\n#lang gflat\nint main() { return 0; }");
        var result = new Preprocessor(new() { IncludeLoader = path => path == header.Path ? header : null }).Process(source);
        Assert.Equal(2, result.ForeignSources.Count);
        Assert.Equal(header.Path, result.ForeignSources[0].Span.Source!.Path);
        Assert.Equal(root, result.ForeignSources[1].Span.Source!.Path);
        Assert.DoesNotContain(result.Tokens, token => token.Text is "from_header" or "still_cpp");
        Assert.Contains(result.Tokens, token => token.Text == "main");
    }

    [Fact]
    public void DocumentationExampleAndEmittedForeignManifestRunThroughTheDriver()
    {
        string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guides", "FOREIGN_SOURCE.md"));
        string code = System.Text.RegularExpressions.Regex.Match(document, @"```gflat\r?\n(.*?)```",
            System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(code);
        string directory = Path.Combine(Path.GetTempPath(), "gflat foreign docs " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.gf"), ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
            File.WriteAllText(source, code);
            Assert.Equal(0, Program.Main([source, "--no-stdlib", "--emit-ir", "-o", ir]));
            Assert.True(File.Exists(ir + ".foreign.json"));
            var compiled = NativeToolchain.Compile(ir, exe);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(exe, []).ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void HeaderDependenciesInvalidateCachedDeclarationsAndSnapshotsStayIndependent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat foreign headers " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "api.h");
            File.WriteAllText(header, "static int    FromHeader(int x) { return x; }");
            DateTime headerTime = File.GetLastWriteTimeUtc(header);
            var source = new SourceFile(Path.Combine(directory, "main.gf"),
                "#lang cpp\n#include \"api.h\"\n#lang gflat\nint main() { return (int)FromHeader(2); }");
            IReadOnlyList<string> dependencies = [];
            var options = new CompilationOptions { NoStdLib = true, DependencyObserver = paths => dependencies = paths,
                ForeignCompilerResolver = () => NativeToolchain.FindClang() };
            var first = Compiler.Check(source, options: options).Ast;
            Assert.Contains(dependencies, path => string.Equals(Path.GetFullPath(path), header, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("int", TypeChecker.TypeName(first.Members.OfType<ast.MethodDeclaration>().Single(method => method.Name == "FromHeader").ReturnType));
            first.Members.Clear();
            File.WriteAllText(header, "static double FromHeader(int x) { return x; }");
            File.SetLastWriteTimeUtc(header, headerTime);
            var second = Compiler.Check(source, options: options).Ast;
            Assert.Equal("double", TypeChecker.TypeName(second.Members.OfType<ast.MethodDeclaration>().Single(method => method.Name == "FromHeader").ReturnType));
            second.Members.Clear();
            var third = Compiler.Check(new SourceFile(source.Path, source.Text + "\n"), options: options).Ast;
            Assert.Contains(third.Members.OfType<ast.MethodDeclaration>(), method => method.Name == "FromHeader");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
