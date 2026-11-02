using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class VariableLookaheadIsolationTests
{
    [Theory]
    [InlineData("Outer<Inner<int>> value;", 0)]
    [InlineData("left < Right<value>>1;", 1)]
    [InlineData("Outer<Inner<int>> value; left < Right<value>>1;", 1)]
    public void NestedTypeProbesPreserveLaterShiftExpressions(string statements, int remainingShifts)
    {
        var tokens = Lexer.Tokenize("int F(){" + statements + "int healthy=42; return healthy;}");
        var original = tokens.ToArray();
        var diagnostics = new DiagnosticBag();
        var unit = Parser.Parse(tokens, diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        Assert.Equal(original, tokens);
        var method = Assert.IsType<MethodDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(remainingShifts, method.Body!.Statements.OfType<ExpressionStatement>()
            .Sum(statement => CountShifts(statement.Expression)));
        Assert.Contains(method.Body!.Statements, statement => statement is VariableDeclaration { Name: "healthy" });
    }

    private static int CountShifts(AstNode node) => node is BinaryExpression binary
        ? (binary.Operator == TokenKind.GreaterGreater ? 1 : 0) + CountShifts(binary.Left) + CountShifts(binary.Right)
        : 0;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedGenericVariablesAndShiftsExecute(int optimization)
    {
        const string source = """
            struct Box<T> { public T Value; public Box(T value){Value=value;} }
            int main(){
                Box<Box<int>> nested=new(new Box<int>(40));
                int value=8;
                value=value>>2;
                return nested.Value.Value+value;
            }
            """;
        Assert.Equal(42, CompilerTestHelper.Run([new SourceFile("variable-lookahead.gf", source)], optimization).ExitCode);
    }
}
