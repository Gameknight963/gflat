using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ParserSpeculationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericCallsKeepTheirOwnedTokenListAcrossStatements(bool recoverErrors)
    {
        const int count = 256;
        string text = "int F(){" + string.Concat(Enumerable.Repeat(
            "if (left < right) value = Read<Container<int>>(value);", count)) + "return 42;}";
        var tokens = Lexer.Tokenize(text);
        var original = tokens.ToArray();
        var diagnostics = new DiagnosticBag();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var unit = Parser.Parse(tokens, diagnostics, recoverErrors);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        Assert.Equal(original, tokens);
        var method = Assert.IsType<MethodDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(count + 1, method.Body!.Statements.Count);
        Assert.IsType<ReturnStatement>(method.Body.Statements[^1]);
        Assert.True(allocated < 16 * 1024 * 1024, $"Parsing allocated {allocated:N0} bytes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("static ")]
    public void RecognizedLambdaBodiesRetainOrdinarySyntaxRecovery(string prefix)
    {
        string text = "int F(){ int()* action = " + prefix +
            "() => { int broken = ; return 0; }; int healthy = 42; return healthy; }";
        var diagnostics = new DiagnosticBag();
        var unit = Parser.Parse(Lexer.Tokenize(text), diagnostics, recoverErrors: true);
        Assert.True(diagnostics.HasErrors);
        var method = Assert.IsType<MethodDeclaration>(Assert.Single(unit.Members));
        var action = Assert.Single(method.Body!.Statements.OfType<VariableDeclaration>(), variable => variable.Name == "action");
        Assert.IsType<LambdaExpression>(action.Initializer);
        Assert.Contains(method.Body.Statements.OfType<VariableDeclaration>(), variable => variable.Name == "healthy");
        Assert.IsType<ReturnStatement>(method.Body.Statements[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarMathParsingDoesNotRecoverThroughFailedTypeProbes(bool recoverErrors)
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core/Numerics/Math.gf"));
        var tokens = Lexer.Tokenize(text);
        var diagnostics = new DiagnosticBag();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var unit = Parser.Parse(tokens, diagnostics, recoverErrors);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        var space = Assert.Single(unit.Namespaces);
        space = Assert.Single(space.Members.OfType<NamespaceDeclaration>());
        var math = Assert.Single(space.Members.OfType<ClassDeclaration>());
        Assert.Equal("Math", math.Name);
        Assert.Contains(math.Members.OfType<MethodDeclaration>(), method => method.Name == "Clamp");
        Assert.True(allocated < 16 * 1024 * 1024, $"Parsing allocated {allocated:N0} bytes");
    }
}
