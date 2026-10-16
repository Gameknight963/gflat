using gflat.ast;

namespace gflat.Tests;

public sealed class NamespaceTypeAccessibilityTests
{
    [Theory]
    [InlineData("class Entry{}")]
    [InlineData("static class Entry{}")]
    [InlineData("abstract class Entry{}")]
    [InlineData("interface Entry{}")]
    [InlineData("static interface Entry{}")]
    [InlineData("abstract interface Entry{}")]
    [InlineData("struct Entry{}")]
    [InlineData("enum Entry{Value}")]
    public void NamespaceTypesDefaultToInternalRegardlessOfOtherModifiers(string declaration)
    {
        foreach (string text in new[] { declaration, "namespace Library{" + declaration + "}" })
        {
            var snapshot = new AnalysisSnapshot([new SourceFile("namespace-type.gf", text)], new() { NoStdLib = true });
            Assert.Empty(snapshot.Diagnostics);
            Assert.Equal(TokenKind.Internal, Accessibility(TypeDeclaration(snapshot)));
        }
    }

    [Theory]
    [InlineData("public", TokenKind.Public)]
    [InlineData("private", TokenKind.Private)]
    [InlineData("protected", TokenKind.Protected)]
    [InlineData("internal", TokenKind.Internal)]
    public void ExplicitTypeAccessibilityIsPreserved(string modifier, TokenKind expected)
    {
        foreach (string declaration in new[] { "class Entry{}", "static class Entry{}", "interface Entry{}", "static interface Entry{}", "struct Entry{}", "enum Entry{Value}" })
        {
            var snapshot = new AnalysisSnapshot([new SourceFile("explicit-type.gf", modifier + " " + declaration)], new() { NoStdLib = true });
            Assert.Empty(snapshot.Diagnostics);
            Assert.Equal(expected, Accessibility(TypeDeclaration(snapshot)));
        }
    }

    [Fact]
    public void OrdinaryNamespaceDeclarationsRetainTheirPublicDefaults()
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("other-defaults.gf", "alias Word=int; int value=0; int Read()=>value; int Value=>value;")], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(TokenKind.Public, Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<AliasDeclaration>()).Accessibility);
        Assert.Equal(TokenKind.Public, Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<FieldDeclaration>(), f => f.Name == "value").Accessibility);
        Assert.Equal(TokenKind.Public, Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<MethodDeclaration>(), m => m.Name == "Read").Accessibility);
        Assert.Equal(TokenKind.Public, Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<PropertyDeclaration>()).Getter!.Accessibility);
    }

    private static AstNode TypeDeclaration(AnalysisSnapshot snapshot)
        => Assert.Single(snapshot.Nodes.Select(n => n.Node), n => n is ClassDeclaration or StructDeclaration or InterfaceDeclaration or EnumDeclaration);

    private static TokenKind Accessibility(AstNode node) => node switch
    {
        ClassDeclaration c => c.Accessibility,
        StructDeclaration s => s.Accessibility,
        InterfaceDeclaration i => i.Accessibility,
        EnumDeclaration e => e.Accessibility,
        _ => throw new InvalidOperationException()
    };
}
