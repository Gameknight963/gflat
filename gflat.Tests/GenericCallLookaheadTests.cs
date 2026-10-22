using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class GenericCallLookaheadTests
{
    [Theory]
    [InlineData("result < 0l || result > (long)42")]
    [InlineData("(result < 0l) || (result > (long)42)")]
    [InlineData("result < limit || result > (long)42")]
    [InlineData("result < limit && result > (long)42")]
    public void ComparisonsBeforeCastsAreNotGenericCalls(string expression)
    {
        var diagnostics = new DiagnosticBag();
        Parser.Parse(Lexer.Tokenize("bool F(long result, long limit){return " + expression + ";}"), diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
    }

    [Theory]
    [InlineData("Use<int>(42)")]
    [InlineData("Use<int(int)*>(callback)")]
    [InlineData("Use<int(int(long)*,int)*>(callback)")]
    [InlineData("Use<Outer<int>::Inner<long>>(value)")]
    [InlineData("Outer<int>::Inner<long>::Run()")]
    [InlineData("Use<int[Count(1 < 2, 3 > 2)]>(value)")]
    [InlineData("Use<Outer<Inner<int>>>(value)")]
    public void RealTypeArgumentsStillParse(string expression)
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parser.Parse(Lexer.Tokenize("int F(){return " + expression + ";}"), diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        Assert.NotEmpty(ast.Members);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComparisonAndGenericCallsExecuteTogether(int optimization)
    {
        const string source = """
            T Identity<T>(T value) => value;
            bool F(long result, long limit) => result < limit || result > (long)42;
            int main() {
                if (!F(-1l, 0l) || F(42l, 0l) || !F(43l, 0l)) return 1;
                return Identity<int>(42);
            }
            """;
        Assert.Equal(42, CompilerTestHelper.Run([new SourceFile("lookahead.gf", source)], optimization).ExitCode);
    }

    [Fact]
    public void FailedLookaheadDoesNotPolluteRecoveryDiagnostics()
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parser.Parse(Lexer.Tokenize("bool F(long result){return result < 0l || result > (long)42;} int Broken( {return 0;} int Healthy()=>42;"), diagnostics, recoverErrors: true);
        Assert.True(diagnostics.HasErrors);
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("LongLiteral"));
        Assert.Contains(ast.Members, member => member is MethodDeclaration { Name: "Healthy" });
    }

    [Theory]
    [InlineData("Use<Outer<Inner<int>>>(value)")]
    [InlineData("left < Right<value>>1")]
    public void LookaheadDoesNotSplitTheCallersShiftTokens(string expression)
    {
        var tokens = Lexer.Tokenize("int F(){return " + expression + ";}");
        int shifts = tokens.Count(token => token.Kind == TokenKind.GreaterGreater);
        var diagnostics = new DiagnosticBag();
        Parser.Parse(tokens, diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        // The successful generic parse itself owns any required split; a failed
        // generic probe must leave the original shift expression untouched.
        if (expression.StartsWith("left"))
            Assert.Equal(shifts, tokens.Count(token => token.Kind == TokenKind.GreaterGreater));
    }
}
