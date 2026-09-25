using gflat.diagnostics;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class SourceLocationTests
{
    [Fact]
    public void IntermediateCallAndMemberNodesKeepTheirOwnRanges()
    {
        const string text = "int main() => Make().field + 1;";
        var tree = Parser.Parse(Lexer.Tokenize(new SourceFile("ranges.gf", text)));
        var method = Assert.IsType<gflat.ast.MethodDeclaration>(tree.Members.Single());
        var statement = Assert.IsType<gflat.ast.ReturnStatement>(method.Body!.Statements.Single());
        var binary = Assert.IsType<gflat.ast.BinaryExpression>(statement.Value);
        var member = Assert.IsType<gflat.ast.MemberAccessExpression>(binary.Left);
        Assert.Equal("Make().field", text.Substring(member.Span.Start, member.Span.Length));
        Assert.Equal("Make()", text.Substring(member.Object.Span.Start, member.Object.Span.Length));
    }

    [Theory]
    [InlineData("int main() => missing;", "missing")]
    [InlineData("int main() { int n = true; return 0; }", "true")]
    [InlineData("int main() { int n = 0; n = true; return n; }", "true")]
    [InlineData("struct S { int n = true; }", "true")]
    [InlineData("int main() { int n = (true\n || false); return 0; }", "(true\n || false)")]
    public void DiagnosticsPreserveTheOffendingExpression(string text, string expected)
    {
        var error = Compiler.Analyze([new SourceFile("range.gf", text)])
            .First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, text.Substring(error.Span.Start, error.Span.Length));
        Assert.Equal("range.gf", error.Span.Source!.Path);
    }

    [Fact]
    public void MissingSemicolonHasAnInsertionRange()
    {
        const string text = "int main() { return 0 }";
        var error = Compiler.Analyze([new SourceFile("range.gf", text)]).First();
        Assert.Equal(text.IndexOf('}'), error.Span.Start);
        Assert.Equal(0, error.Span.Length);
    }

    [Fact]
    public void TokensRetainFileAndUtf16Offsets()
    {
        var source = new SourceFile("src/test.gf", "// hello\r\n  int value;");
        var token = Lexer.Tokenize(source)[0];
        Assert.Equal(source.Id, token.Span.SourceId);
        Assert.Equal(2, token.Line);
        Assert.Equal(3, token.Column);
        Assert.Equal("int", source.Text.Substring(token.Span.Start, token.Span.Length));
    }

    [Fact]
    public void ParserRecordsDeclarationRange()
    {
        var source = new SourceFile("main.gf", "\nint main() => 42;");
        var ast = Parser.Parse(Lexer.Tokenize(source));
        var method = ast.Members.OfType<gflat.ast.MethodDeclaration>().Single(m => m.Name == "main");
        Assert.Equal(source.Id, method.Span.SourceId);
        Assert.Equal("int main() => 42;", source.Text.Substring(method.Span.Start, method.Span.Length));
    }

    [Theory]
    [InlineData("int main() {\n    return missing;\n}", 2)]
    [InlineData("int main() {\n    return 1\n}", 3)]
    [InlineData("\n/* unfinished", 2)]
    public void DiagnosticsIdentifyNamedUnsavedSource(string text, int line)
    {
        var bag = new DiagnosticBag();
        Assert.Throws<TypeCheckException>(() => Compiler.Check(new SourceFile("unsaved.gf", text), bag));
        var error = bag.Items.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("unsaved.gf", error.FilePath);
        Assert.Equal(line, error.Line);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public async Task ConcurrentCompilationsKeepSourceLocationsSeparate()
    {
        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            var bag = new DiagnosticBag();
            Assert.Throws<TypeCheckException>(() => Compiler.Check(new SourceFile($"file{i}.gf", "int main() => missing;"), bag));
            return bag.Items[0].FilePath;
        })));
        Assert.Equal(8, paths.Distinct().Count());
    }
}
