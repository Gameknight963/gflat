using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExternDeclarationModifierTests
{
    [Theory]
    [InlineData("public static extern int Foreign(int value);")]
    [InlineData("namespace Library{static private extern int Foreign(int value);}")]
    public void NamespaceExternCannotDiscardAStaticModifier(string text)
    {
        var source = new SourceFile("extern-modifier.gf", text);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Equal("extern declarations accept only accessibility modifiers", error.Description);
        Assert.Equal("static", text.Substring(error.Span.Start, error.Span.Length));
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Equal(error.Description, Assert.Single(snapshot.Diagnostics).Message);
        Assert.Equal(error.Span, snapshot.Diagnostics[0].Span);
        Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<ExternDeclaration>());
    }

    [Theory]
    [InlineData("private extern")]
    [InlineData("extern private")]
    public void PrivateExternFormsKeepTheirDeclaredAccessibility(string modifiers)
    {
        var (ast, _) = Compiler.Check(modifiers + " int Foreign(int value);", options: new() { NoStdLib = true });
        Assert.Equal(TokenKind.Private, Assert.Single(ast.Members.OfType<ExternDeclaration>()).Accessibility);
    }
}
