using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ExpressionLookaheadIsolationTests
{
    [Theory]
    [InlineData("(left < Right<value>>1)", 1)]
    [InlineData("(left < Right<value>>1) + (left < Right<value>>2)", 2)]
    [InlineData("(Outer<Inner<int>>)value", 0)]
    [InlineData("(Outer<Inner<int>> value)=>value", 0)]
    [InlineData("static (Outer<Inner<int>> value)=>value", 0)]
    [InlineData("Use<int(Outer<Inner<int>>)*>(callback)", 0)]
    public void ExpressionProbesPreserveCallerTokensAndLaterShifts(string expression, int shifts)
    {
        var tokens = Lexer.Tokenize("void F(){" + expression + "; left < Right<value>>3;}");
        var original = tokens.ToArray();
        var diagnostics = new DiagnosticBag();
        var unit = Parser.Parse(tokens, diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        Assert.Equal(original, tokens);
        var method = Assert.IsType<MethodDeclaration>(Assert.Single(unit.Members));
        var statements = method.Body!.Statements.OfType<ExpressionStatement>().ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Equal(shifts, CountShifts(statements[0].Expression));
        Assert.Equal(1, CountShifts(statements[1].Expression));
    }

    private static int CountShifts(AstNode node) => node is BinaryExpression binary
        ? (binary.Operator == TokenKind.GreaterGreater ? 1 : 0) + CountShifts(binary.Left) + CountShifts(binary.Right)
        : 0;
}
