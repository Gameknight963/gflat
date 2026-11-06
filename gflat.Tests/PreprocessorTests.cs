using gflat.CompileExceptions;
using gflat.Preprocessing;

namespace gflat.Tests;

public sealed class PreprocessorTests
{
    private static PreprocessingResult Process(string code, CompilationOptions? options = null)
        => new Preprocessor(options).Process(new SourceFile("macros.gf", code));
    private static string[] Words(string code) => Process(code).Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text).ToArray();

    [Fact]
    public void MacroDescriptorsCarryClassifiedDeclarationsAndExpansions()
    {
        const string code = "#define VERSION 12\n#define ST(x) #x\n#define JOIN(left, right) left##right\nST(VERSION) JOIN(4, 2)";
        var result = Process(code);
        foreach (var symbol in result.Symbols.Where(s => s.Definition))
            Assert.Equal(symbol.Signature, string.Concat(symbol.DeclarationTokens!.Select(t => t.Text)));
        var version = Assert.Single(result.Symbols, s => s.Name == "VERSION" && s.Definition);
        var number = Assert.Single(version.DeclarationTokens!, t => t.Text == "12");
        Assert.Equal("number", number.Classification);
        Assert.Equal(code.IndexOf("12", StringComparison.Ordinal), number.Span.Start);
        var join = Assert.Single(result.Symbols, s => s.Name == "JOIN" && s.Definition);
        Assert.All(join.DeclarationTokens!.Where(t => t.Text is "left" or "right"), t => Assert.Equal("identifier", t.Classification));
        Assert.Equal("operator", Assert.Single(join.DeclarationTokens!, t => t.Text == "##").Classification);
        foreach (var symbol in result.Symbols.Where(s => s.ExpansionTokens != null))
            Assert.Equal(symbol.Expansion, string.Concat(symbol.ExpansionTokens!.Select(t => t.Text)));
        Assert.Equal("string", Assert.Single(result.Symbols.Single(s => s.Name == "ST" && !s.Definition).ExpansionTokens!).Classification);
        Assert.Equal("number", Assert.Single(result.Symbols.Single(s => s.Name == "JOIN" && !s.Definition).ExpansionTokens!).Classification);
    }

    [Fact]
    public void ClassificationRespectsEscapesParameterShadowingAndUninvokedFragments()
    {
        var result = Process("#define left 12\n#define VERSION 7\n#define JOIN(left, right) left##right\n#define RAW @VERSION\n#define FRAGMENT (\n");
        var join = result.Symbols.Single(s => s.Name == "JOIN" && s.Definition);
        Assert.All(join.DeclarationTokens!.Where(t => t.Text == "left"), t => Assert.Equal("identifier", t.Classification));
        var raw = result.Symbols.Single(s => s.Name == "RAW" && s.Definition);
        Assert.Equal("#define RAW @VERSION", raw.Signature);
        Assert.Equal("identifier", Assert.Single(raw.DeclarationTokens!, t => t.Text == "VERSION").Classification);
    }

    [Fact]
    public void ClassifiedExpansionTruncationKeepsTheSamePreviewText()
    {
        var result = Process("#define LONG \"" + new string('a', 3000) + "\"\nLONG");
        var use = result.Symbols.Single(s => s.Name == "LONG" && !s.Definition);
        Assert.Equal(2049, use.Expansion!.Length);
        Assert.EndsWith("…", use.Expansion);
        Assert.Equal(use.Expansion, string.Concat(use.ExpansionTokens!.Select(t => t.Text)));
        Assert.Equal("string", use.ExpansionTokens![0].Classification);
    }

    [Fact]
    public void ExpansionPreviewsFollowInvocationTimeDefinitionsAndPreserveArgumentPreviews()
    {
        const string code = "#define V 7\n#define TWICE(x) (x + x)\nTWICE(V)\n#undef V\n#define V 9\nTWICE(V)";
        var result = Process(code);
        var invocations = result.Symbols.Where(s => s.Name == "TWICE" && !s.Definition).ToArray();
        Assert.Equal(new[] { "( 7 + 7 )", "( 9 + 9 )" }, invocations.Select(s => s.Expansion));
        Assert.Equal("7", Assert.Single(result.Symbols, s => s.Span.Start == code.IndexOf("TWICE(V)", StringComparison.Ordinal) + 6).Expansion);
    }

    [Theory]
    [InlineData("#define A 12\nA", "12")]
    [InlineData("#define A B\n#define B A\nA", "A")]
    [InlineData("#define F(x) x\nF(F(42))", "42")]
    [InlineData("#define F G\n#define G(x) x\nF(42)", "42")]
    [InlineData("#define F (42)\nF", "( 42 )")]
    [InlineData("#define V 12\n#define S(x) #x\nS(V)", "\"12\"")]
    [InlineData("#define V 12\n#define S(x) #x\nS(@V)", "\"V\"")]
    [InlineData("#define V 12\n#define S(x) #x\nS(@(V + V))", "\"V + V\"")]
    [InlineData("#define V 12\n#define S(x) #x\n#define F(x) S(x)\nF(@V)", "\"12\"")]
    [InlineData("#define V 12\n#define S(x) #x\n#define F(x) S(x)\nF(@(V + V))", "\"12 + 12\"")]
    [InlineData("#define V 12\n#define J(a,b) a##b\nJ(item_,V)", "item_12")]
    [InlineData("#define V 12\n#define item_V 42\n#define J(a,b) a##b\nJ(item_,@V)", "42")]
    [InlineData("#define E\n#define J(a,b) a##b\nJ(E,token)", "token")]
    [InlineData("#define F(...) __VA_OPT__(present)\nF() F(1)", "present")]
    [InlineData("#define E\n#define F(...) __VA_OPT__(present)\nF(E)", "")]
    [InlineData("#define F(a,...) a __VA_OPT__(,) __VA_ARGS__\nF(1) F(2,3,4)", "1 2 , 3 , 4")]
    [InlineData("#define V 12\n\"V\" 'V' // V\n/* V */ V", "\"V\" 'V' 12")]
    [InlineData("#define V 1\\\n2\nV", "12")]
    [InlineData("#define V 1\n#define V 1\n#undef V\n#define V 2\nV", "2")]
    public void ExpansionFollowsTokenRules(string code, string expected)
        => Assert.Equal(expected, string.Join(" ", Words(code)));

    [Theory]
    [InlineData("1 + 2 * 3 == 7")]
    [InlineData("!defined(MISSING) && defined(V)")]
    [InlineData("(0 && (1 / 0)) == 0")]
    [InlineData("1 || (1 / 0)")]
    [InlineData("(1 ? 42 : (1 / 0)) == 42")]
    [InlineData("(0 ? (1 / 0) : 42) == 42")]
    [InlineData("(0xffffffffffffffffu > 0) && (-1 < 0)")]
    [InlineData("(-1 < 1u) == 0")]
    [InlineData("(1 << 5) == 32 && (32 >> 3) == 4")]
    [InlineData("'\u00ff' == -1")]
    [InlineData("'A' == 65 && '\\n' == 10")]
    public void ConditionalsUseIntegerPrecedenceAndShortCircuit(string condition)
    {
        Assert.Equal(["selected"], Words($"#define V 1\n#if {condition}\nselected\n#else\nwrong\n#endif"));
    }

    [Fact]
    public void DisabledBranchesAndNestedConditionalsDoNotExpandOrInclude()
    {
        Assert.Equal(["selected"], Words("""
            #if 0
            #include "missing.gfh"
            #error disabled
            invalid language ?? ? ;
            #if 1/0
            #endif
            #elif 0
            wrong
            #else
            selected
            #endif
            """));
    }

    [Theory]
    [InlineData("#define X 1\n#define X 2", "redefinition")]
    [InlineData("#define F(x,x) x", "parameter")]
    [InlineData("#define F(x) #missing", "parameter")]
    [InlineData("#define F(x) x##", "##")]
    [InlineData("#define F(x) x\nF(1,2)", "arguments")]
    [InlineData("#define F(x) x\nF(1", "Unterminated")]
    [InlineData("#if 1", "Unterminated")]
    [InlineData("#else", "matching")]
    [InlineData("#if 1\n#else\n#else\n#endif", "follows")]
    [InlineData("#if 1/0\n#endif", "zero")]
    [InlineData("#define F(a,b) a##b\nF(+,*)", "paste")]
    [InlineData("#error deliberate failure", "deliberate failure")]
    [InlineData("#define F(a,b) a##b\nF(/,*)", "paste")]
    [InlineData("#if '\u0100'\n#endif", "8-bit")]
    [InlineData("#define F(...) __VA_OPT__", "Expected")]
    [InlineData("#if\n#endif", "requires")]
    public void InvalidPreprocessingHasLocatedDiagnostics(string code, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Process(code));
        Assert.Contains(message, error.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("macros.gf", error.Span.Source?.Path);
    }

    [Fact]
    public void IncludesShareMacrosAndSupportGuardsOnceTransitivesAndCodeFragments()
    {
        string directory = Path.GetFullPath("virtual-preprocessor");
        var files = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        void Header(string name, string code) { string path = Path.Combine(directory, name); files[path] = new(path, code); }
        Header("guard.gfh", "#ifndef GUARD\n#define GUARD\n#include \"once.gfh\"\n#define RESULT INPUT\n#endif");
        Header("once.gfh", "#pragma once\nint Function(){return\n#include \"value.gfh\"\n;}\n");
        Header("value.gfh", "INPUT");
        var root = new SourceFile(Path.Combine(directory, "main.gf"), "#define INPUT 42\n#include \"guard.gfh\"\n#include \"guard.gfh\"\n#include \"once.gfh\"\nint main()=>Function()+RESULT-42;");
        var options = new CompilationOptions { NoStdLib = true, IncludeLoader = path => files.GetValueOrDefault(path) };
        string ir = Compiler.Emit([root], options: options);
        Assert.Contains("define i32 @main", ir);
        Assert.Equal(1, ir.Split("define i32 @gflat$Function(").Length - 1);
    }

    [Fact]
    public void IncludeSearchDistinguishesQuotesAnglesAndExpandedNames()
    {
        string local = Path.GetFullPath("virtual-local"), exported = Path.GetFullPath("virtual-include");
        var files = new Dictionary<string, SourceFile>();
        files[Path.Combine(local, "value.gfh")] = new(Path.Combine(local, "value.gfh"), "local");
        files[Path.Combine(exported, "value.gfh")] = new(Path.Combine(exported, "value.gfh"), "exported");
        var result = new Preprocessor(new() { IncludeDirectories = [exported], IncludeLoader = path => files.GetValueOrDefault(path) })
            .Process(new(Path.Combine(local, "root.gf"), "#include \"value.gfh\"\n#include <value.gfh>\n#define FILE \"value.gfh\"\n#include FILE"));
        Assert.Equal(["local", "exported", "local"], result.Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text));
    }

    [Fact]
    public void RootsHaveIndependentMacroAndPragmaOnceState()
    {
        var sources = new[] { new SourceFile("a.gf", "#define FLAG 1\nint A()=>FLAG;"),
            new SourceFile("b.gf", "#ifdef FLAG\n#error leaked\n#endif\nint main()=>A();") };
        _ = Compiler.Emit(sources, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExpandedCodeAndVerbatimIdentifiersExecute(int optimization)
    {
        const string code = """
            #define VERSION 12
            #define STRINGIFY(value) #value
            #define CHECK(condition) if(!(condition))return 1;
            struct @class { public int @int; }
            int main(){
                @class @return; @return.@int=42;
                readonly(char)* first=STRINGIFY(VERSION);
                readonly(char)* second=STRINGIFY(@VERSION);
                CHECK(@(VERSION > 0))
                if(first[0]!='1'||first[1]!='2'||second[0]!='V')return 2;
                return @return.@int;
            }
            """;
        _ = CompilerTestHelper.RunIr(Compiler.Emit(code, options: new() { NoStdLib = true }), optimization, expectedExitCode: 42);
    }

    [Fact]
    public void VerbatimExternNamesAndNameofUseTheIdentifierName()
    {
        string ir = Compiler.Emit("extern \"C\" int @class(); int main(){readonly(char)* text=nameof(@class);return @class();}", options: new() { NoStdLib = true });
        Assert.Contains("@class()", ir);
        Assert.DoesNotContain("@@class", ir);
        Assert.Contains("\\63\\6C\\61\\73\\73\\00", ir);
    }
    [Fact]
    public void InterpolationExpandsOnlyExpressionHolesAndRetainsLiteralPrefixes()
    {
        var tokens = Process("#define VALUE 42\ns$\"VALUE {VALUE} {\"VALUE\"}\"").Tokens;
        Assert.Contains(tokens, token => token.Kind == TokenKind.IntLiteral && token.Text == "42");
        Assert.Contains(tokens, token => token.Kind == TokenKind.InterpolatedStringSegment && token.Text == "VALUE ");
        Assert.Contains(tokens, token => token.Kind == TokenKind.StringLiteral && token.Text == "\"VALUE\"");
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("s", tokens[0].Text);
    }

    [Fact]
    public void NestedGenericClosersFromMacrosRetainParserAdjacency()
    {
        const string code = "#define BOX Box<Box<int>>\nstruct Box<T>{public T Value;} int main(){BOX value=new();return 42;}";
        Assert.Contains("define i32 @main", Compiler.Emit(code, options: new() { NoStdLib = true }));
    }

    [Fact]
    public void IncludeErrorsAndExpandedArgumentsKeepPhysicalSourceLocations()
    {
        string root = Path.GetFullPath("virtual-location/main.gf"), header = Path.GetFullPath("virtual-location/body.gfh");
        var snapshot = new AnalysisSnapshot([new(root, "#include \"body.gfh\"")], new() { NoStdLib = true,
            IncludeLoader = path => path == header ? new(header, "#define ID(x) x\nint main()=>ID(missing);") : null });
        var error = Assert.Single(snapshot.Diagnostics, diagnostic => diagnostic.Severity == diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(header, error.FilePath);
        Assert.Equal(2, error.Span.Line);
        Assert.Equal("missing", error.Span.Source!.Text.Substring(error.Span.Start, error.Span.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GuideExampleCompilesAndPrintsTheDocumentedOutput(int optimization)
    {
        string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guides/PREPROCESSOR.md"));
        var examples = System.Text.RegularExpressions.Regex.Matches(document, "```cpp\\r?\\n(.*?)```", System.Text.RegularExpressions.RegexOptions.Singleline);
        string header = Path.GetFullPath("virtual-guide/features.gfh");
        string ir = Compiler.Emit([new SourceFile(Path.GetFullPath("virtual-guide/main.gf"), examples[1].Groups[1].Value)], options: new()
        {
            NoStdLib = true,
            IncludeLoader = path => path == header ? new(header, examples[0].Groups[1].Value) : null
        });
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.Equal("VERSION = 12, answer = 42", result.StandardOutput.Trim());
    }

    [Fact]
    public void InterpolationHoleTokensRetainTheirSourceRangesAfterExpansion()
    {
        const string code = "#define VALUE 42\ns$\"text {missing} {VALUE}\"";
        var tokens = Process(code).Tokens;
        var name = Assert.Single(tokens, token => token.Text == "missing");
        Assert.Equal(code.IndexOf("missing", StringComparison.Ordinal), name.Start);
        Assert.Equal(7, name.Span.Length);
        var expanded = Assert.Single(tokens, token => token.Kind == TokenKind.IntLiteral);
        Assert.Equal(code.LastIndexOf("VALUE", StringComparison.Ordinal), expanded.Start);
        Assert.Equal(5, expanded.Span.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MacroParametersInsideInterpolationHolesExecute(int optimization)
    {
        const string code = """
            #define VALUE 42
            #define RENDER(value) s$"value {value}"
            using Standard;
            using Standard::Text;

            extern "C" int printf(readonly(char)* format, ...);
            int main()
            {
                String text = RENDER(VALUE);
                printf("%s", text.Data);
                return 0;
            }
            """;
        var result = CompilerTestHelper.Run(InterpolationTests.Sources(code), optimization);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("value 42", result.StandardOutput);
    }

}
