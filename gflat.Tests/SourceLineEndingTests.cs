using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class SourceLineEndingTests
{
    [Fact]
    public void MixedLineEndingsPreserveOriginalOffsetsIncludingDelimiterBytesAndEof()
    {
        const string text = "a\rb\r\nc\nd\r";
        var source = new SourceFile("mixed.gf", text);
        (int Line, int Column)[] expected = [(1,1),(1,2),(2,1),(2,2),(2,3),(3,1),(3,2),(4,1),(4,2),(5,1)];
        Assert.Same(text, source.Text);
        for (int offset = 0; offset <= text.Length; offset++)
        {
            SourceSpan span = source.Span(offset);
            Assert.Equal(offset, span.Start);
            Assert.Equal(0, span.Length);
            Assert.Equal(expected[offset].Line, span.Line);
            Assert.Equal(expected[offset].Column, span.Column);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void LineSpansExcludeTerminatorsAndDoNotTrimIntoTheNextLine(string newline)
    {
        string text = "  first" + newline + " \t" + newline + "  last" + newline;
        var source = new SourceFile("lines.gf", text);
        SourceSpan first = source.LineSpan(1), blank = source.LineSpan(2), last = source.LineSpan(3), eof = source.LineSpan(4);
        Assert.Equal((2,5,1,3), (first.Start,first.Length,first.Line,first.Column));
        Assert.Equal((9+newline.Length,0,2,3), (blank.Start,blank.Length,blank.Line,blank.Column));
        Assert.Equal((11+2*newline.Length,4,3,3), (last.Start,last.Length,last.Line,last.Column));
        Assert.Equal((text.Length,0,4,1), (eof.Start,eof.Length,eof.Line,eof.Column));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void LineCommentsEndAtEveryLogicalNewlineAndEofStartsTheNextLine(string newline)
    {
        string text = "// ignored" + newline + "int F()=>1;" + newline;
        Token[] tokens = Lexer.Tokenize(new SourceFile("comment.gf", text)).ToArray();
        Assert.Equal(TokenKind.Int, tokens[0].Kind);
        Assert.Equal((10+newline.Length,2,1), (tokens[0].Start,tokens[0].Line,tokens[0].Column));
        Assert.Equal((text.Length,3,1), (tokens[^1].Start,tokens[^1].Line,tokens[^1].Column));
        Compiler.Check(text, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void BlockAndInterpolationCommentsUseTheSameLogicalLineBoundaries(string newline)
    {
        string block = "/* first" + newline + "second */" + newline + "int value;";
        Token first = Lexer.Tokenize(new SourceFile("block.gf", block))[0];
        Assert.Equal(TokenKind.Int, first.Kind);
        Assert.Equal((17+2*newline.Length,3,1), (first.Start,first.Line,first.Column));
        string invalid = block + newline + "@";
        var error = Assert.Throws<TypeCheckException>(() => Lexer.Tokenize(new SourceFile("block-error.gf", invalid)));
        Assert.Equal((invalid.Length-1,4,1), (error.Span.Start,error.Line,error.Span.Column));
        string interpolation = "$\"{1 // } ignored" + newline + " +2}\";" + newline + "int after;";
        Token after = Assert.Single(Lexer.Tokenize(new SourceFile("interpolation.gf", interpolation)),
            token => token.Kind == TokenKind.Identifier && token.Text == "after");
        Assert.Equal((3,5), (after.Line,after.Column));
        Assert.Equal(interpolation.Length-6, after.Start);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void MissingClosingBraceUsesTheEmptyFinalLineInsertionPoint(string newline)
    {
        string text = "int main(){" + newline;
        var diagnostic = Assert.Single(Compiler.Analyze([new SourceFile("eof.gf", text)], new() { NoStdLib = true }),
            item => item.Severity == DiagnosticSeverity.Error);
        Assert.Equal((text.Length,0,2,1), (diagnostic.Span.Start,diagnostic.Span.Length,diagnostic.Line,diagnostic.Column));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void RecoveryAndCompilerDiagnosticsKeepOffsetsAfterNewlines(string newline)
    {
        string broken = "\"bad" + newline + "int good;";
        var bag = new DiagnosticBag();
        var tokens = Lexer.Tokenize(new SourceFile("recovery.gf", broken), bag);
        Assert.Single(bag.Items);
        Assert.Equal((0,1,1), (bag.Items[0].Span.Start,bag.Items[0].Line,bag.Items[0].Column));
        Token good = Assert.Single(tokens, token => token.Kind == TokenKind.Identifier && token.Text == "good");
        Assert.Equal((8+newline.Length,2,5), (good.Start,good.Line,good.Column));
        string code = "int F()=>0;" + newline + "int G()=>missing;";
        var diagnostic = Assert.Single(Compiler.Analyze([new SourceFile("diagnostic.gf", code)], new() { NoStdLib = true }),
            item => item.Severity == DiagnosticSeverity.Error);
        Assert.Equal((20+newline.Length,7,2,10), (diagnostic.Span.Start,diagnostic.Span.Length,diagnostic.Line,diagnostic.Column));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MixedNewlineProgramsExecuteCommentsAndFollowingCode(int optimization)
    {
        const string source = "int Twice(int value){\r// body\rreturn value*2;\r}\r\nint main(){\n/* first\rsecond */\rreturn Twice(21)-42;\r}";
        string directory = Path.Combine(Path.GetTempPath(), "gflat-line-endings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "source with spaces.gf");
            File.WriteAllText(path, source);
            string ir = Compiler.Emit([new SourceFile(path, source)], options: new() {
                NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true, DebugSymbols = true });
            Assert.Contains("!DILocation(line: 8,", ir);
            Assert.Contains(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), ir);
            string irPath = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "main.exe");
            File.WriteAllText(irPath, ir);
            var compiled = NativeToolchain.Compile(irPath, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.DoesNotContain("invalid debug", compiled.StandardError, StringComparison.OrdinalIgnoreCase);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
            if (OperatingSystem.IsWindows()) Assert.True(File.Exists(Path.ChangeExtension(executable, ".pdb")));
            Assert.Equal(source, File.ReadAllText(path));
        }
        catch (Exception error) { error.Data["SourceLineEndingDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
}
