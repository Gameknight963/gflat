using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class TypedLocalDiagnosticTests
{
    [Theory]
    [InlineData("int* alias=null;","alias")]
    [InlineData("int alias=1;","alias")]
    [InlineData("readonly(int)* alias=null;","alias")]
    [InlineData("Outer<Inner<int>> alias;","alias")]
    [InlineData("int* =null;","=")]
    [InlineData("for(int* alias=null;false;){}","alias")]
    public void CleanTypeMissingNameReportsExistingExpectedIdentifierAtActualToken(string statement,string target)
    {
        var source=new SourceFile("typed-local.gf","int F(){"+statement+"int healthy=42;return healthy;}int Later()=>42;");
        var diagnostics=new DiagnosticBag();
        var unit=Parser.Parse(Lexer.Tokenize(source),diagnostics,recoverErrors:true);
        var error=Assert.Single(diagnostics.Items,d=>d.Severity==DiagnosticSeverity.Error);
        Assert.Equal("GF0005",error.Descriptor.Id);
        Assert.Contains("Expected Identifier",error.Message);
        Assert.Equal(source.Text.IndexOf(target,StringComparison.Ordinal),error.Span.Start);
        var method=Assert.IsType<MethodDeclaration>(unit.Members[0]);
        Assert.Contains(method.Body!.Statements,statement=>statement is VariableDeclaration{Name:"healthy"});
        Assert.Contains(unit.Members,node=>node is MethodDeclaration{Name:"Later"});
    }

    [Theory]
    [InlineData("a * new Thing();")]
    [InlineData("f<T>(args);")]
    [InlineData("f<Outer<Inner<int>>>(args);")]
    [InlineData("Outer<Inner<int>> value;left < Right<value>>1;")]
    [InlineData("a * new Thing();f<Outer<Inner<int>>>(args);left < Right<value>>1;")]
    public void SuccessfulExpressionFallbackWinsAndTokenSplitsDoNotCorruptLaterStatements(string statements)
    {
        var tokens=Lexer.Tokenize("int F(){"+statements+"int healthy=42;return healthy;}");
        var diagnostics=new DiagnosticBag();var unit=Parser.Parse(tokens,diagnostics);
        Assert.False(diagnostics.HasErrors,diagnostics.FormatAll());
        var method=Assert.IsType<MethodDeclaration>(Assert.Single(unit.Members));
        Assert.Contains(method.Body!.Statements,node=>node is VariableDeclaration{Name:"healthy"});
        if(statements.StartsWith("a *",StringComparison.Ordinal)) {
            var expression=Assert.IsType<ExpressionStatement>(method.Body.Statements[0]);
            var multiply=Assert.IsType<BinaryExpression>(expression.Expression);
            Assert.Equal(TokenKind.Star,multiply.Operator);Assert.IsType<NewExpression>(multiply.Right);
        }
        if(statements.StartsWith("f<",StringComparison.Ordinal)) {
            var expression=Assert.IsType<ExpressionStatement>(method.Body.Statements[0]);
            Assert.IsType<CallExpression>(expression.Expression);
        }
    }

    [Theory]
    [InlineData("a + ;",";")]
    [InlineData("a * new Thing(,);",",")]
    public void LaterExpressionFailureWinsOverEarlierMissingNameCandidate(string statement,string target)
    {
        var source=new SourceFile("expression-failure.gf","int F(){"+statement+"int healthy=42;return healthy;}");
        var diagnostics=new DiagnosticBag();Parser.Parse(Lexer.Tokenize(source),diagnostics,recoverErrors:true);
        var error=diagnostics.Items.First(d=>d.Severity==DiagnosticSeverity.Error);
        Assert.Equal("GF0004",error.Descriptor.Id);
        Assert.Equal(source.Text.IndexOf(target,StringComparison.Ordinal),error.Span.Start);
        Assert.Contains("Unexpected token",error.Message);
    }
}
