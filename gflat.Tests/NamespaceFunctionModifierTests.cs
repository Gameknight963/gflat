using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NamespaceFunctionModifierTests
{
    private const string Message = "Namespace functions cannot be abstract";

    [Theory]
    [InlineData("public abstract int Read()=>42;")]
    [InlineData("internal abstract int Read(){return 42;}")]
    [InlineData("namespace Library{public abstract int Read()=>42;}")]
    [InlineData("namespace Library{public static abstract int Read()=>42;}")]
    [InlineData("public abstract int Read<T>(T value)=>42;")]
    [InlineData("public inline abstract int Read()=>42;")]
    public void FreeAbstractFunctionsAreRejectedAtTheModifier(string text)
    {
        var source = new SourceFile("namespace-function-modifier.gf", text);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Equal(Message, error.Description);
        Assert.Equal("abstract", text.Substring(error.Span.Start, error.Span.Length));
        var diagnostic = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }));
        Assert.Equal(Message, diagnostic.Message);
        Assert.Equal(error.Span, diagnostic.Span);
    }

    [Fact]
    public void EditorRecoveryRetainsTheRecognizedMethodAndTypedBody()
    {
        var source = new SourceFile("namespace-function-recovery.gf", "public abstract int Read(){int later=42;return later;}");
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Equal(Message, Assert.Single(snapshot.Diagnostics).Message);
        Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<MethodDeclaration>());
        var local = Assert.Single(snapshot.Nodes.Select(n => n.Node).OfType<VariableDeclaration>());
        Assert.Equal("int", Assert.IsType<NamedTypeExpression>(snapshot.Checker!.GetType(local)).Name);
        Assert.Throws<TypeCheckException>(() => Compiler.Emit([source], options: new() { NoStdLib = true }));
    }

    [Fact]
    public void ConcreteNamespaceAndAbstractInstanceFunctionsRetainTheirContracts()
        => Compiler.Check("""
            public const int Fold()=>42;
            namespace Library{public static int Read()=>42;}
            abstract class Base{public abstract int Read();}
            class Derived:Base{public override int Read()=>42;}
            expect int Required();
            replace int Required()=>42;
            int main()=>Fold()+Library::Read()+Required();
            """, options: new() { NoStdLib = true });
}
