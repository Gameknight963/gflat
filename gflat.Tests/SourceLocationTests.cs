using gflat.diagnostics;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class SourceLocationTests
{
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
