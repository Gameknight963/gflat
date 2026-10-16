using gflat.CompileExceptions;
using gflat.ast;

namespace gflat.Tests;

public sealed class NonCallableDeclarationModifierTests
{
    public static IEnumerable<object[]> IgnoredModifiers()
    {
        foreach (string declaration in new[] { "class C{}", "struct S{}", "interface I{}", "enum E{A}", "alias Word=int;" })
            foreach (string modifier in new[] { "const", "readonly" })
            {
                yield return ["public " + modifier + " " + declaration, modifier, declaration.Split(' ')[0]];
                yield return ["namespace Library{ " + modifier + " public " + declaration + " }", modifier, declaration.Split(' ')[0]];
            }
        foreach (string declaration in new[] { "struct S{}", "enum E{A}", "alias Word=int;" })
            yield return ["public abstract " + declaration, "abstract", declaration.Split(' ')[0]];
        yield return ["abstract struct S{}", "abstract", "struct"];
        yield return ["abstract enum E{A}", "abstract", "enum"];
        yield return ["public static alias Word=int;", "static", "alias"];
        foreach (string owner in new[] { "class", "struct" })
            foreach (string declaration in new[] { "class Inner{}", "struct Inner{}", "enum Inner{A}", "alias Word=int;" })
                foreach (string modifier in new[] { "virtual", "override" })
                    yield return [owner + " Outer<T>{public " + modifier + " " + declaration + "}", modifier, declaration.Split(' ')[0]];
        yield return ["class Outer{public abstract struct Inner{}}", "abstract", "struct"];
        yield return ["class Outer{public const class Inner{}}", "const", "class"];
        yield return ["class Outer{public readonly enum Inner{A}}", "readonly", "enum"];
        yield return ["public abstract readonly class C{}", "readonly", "class"];
        yield return ["public weak readonly alias Selected=int;", "readonly", "alias"];
    }

    [Theory]
    [MemberData(nameof(IgnoredModifiers))]
    public void UnsupportedModifiersPointAtTheDiscardedKeyword(string text, string modifier, string declaration)
    {
        var source = new SourceFile("noncallable-modifier.gf", text);
        string message = $"The '{modifier}' modifier is not valid on {declaration} declarations";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Equal(message, error.Description);
        Assert.Equal(modifier, text.Substring(error.Span.Start, error.Span.Length));
        var diagnostic = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }), d => d.Message == message);
        Assert.Equal(error.Span, diagnostic.Span);
    }

    [Fact]
    public void SupportedTypeAndProviderModifiersRemainAvailable()
        => Compiler.Check("""
            public abstract class Base{}
            public static class Values {public static int Read()=>42; public abstract class Nested{} }
            public struct Plain{}
            public abstract interface IInstance{}
            public static interface IProvider{int Read();}
            public static class Provider:IProvider{public static int Read()=>42;}
            public weak alias Selected:IProvider=Provider;
            public expect alias Required:IProvider;
            replace alias Required=Provider;
            public alias View=readonly(int)*;
            int main(){int value=42;View pointer=&value;return Selected::Read()+Required::Read()-*pointer;}
            """, options: new() { NoStdLib = true });

    [Theory]
    [InlineData("readonly class C{public int Read(){int later=7;return later;}}", typeof(ClassDeclaration))]
    [InlineData("const struct C{public int Read(){int later=7;return later;}}", typeof(StructDeclaration))]
    [InlineData("abstract enum C{A} int Read(){int later=7;return later;}", typeof(EnumDeclaration))]
    [InlineData("readonly alias C=int; int Read(){C later=7;return later;}", typeof(AliasDeclaration))]
    public void EditorRecoveryRetainsKnownDeclarationShapeButCompilationRejectsIt(string text, Type declarationType)
    {
        var source = new SourceFile("recover-noncallable-modifier.gf", text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Single(snapshot.Diagnostics);
        Assert.Contains(snapshot.Nodes, n => n.Node.GetType() == declarationType);
        var local = Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>());
        Assert.Equal("int", Assert.IsType<NamedTypeExpression>(snapshot.Checker!.ResolveAlias(snapshot.Checker.GetType(local))).Name);
        Assert.Throws<TypeCheckException>(() => Compiler.Emit([source], options: new() { NoStdLib = true }));
    }
}
