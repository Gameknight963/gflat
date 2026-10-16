using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ConstructorNameSyntaxTests
{
    [Theory]
    [InlineData("S*")]
    [InlineData("S^")]
    [InlineData("S[1]")]
    [InlineData("S()*")]
    [InlineData("Other::S")]
    [InlineData("S<int>")]
    [InlineData("readonly(S)")]
    public void ConstructorsRequirePlainNames(string name)
    {
        string source = "class S{public " + name + "(){}}int main()=>0;";
        var diagnostics = Compiler.Analyze([new SourceFile("constructor-name.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("A constructor declaration requires a plain, unqualified name without type arguments", error.Message);
        Assert.Equal(name, source.Substring(error.Span.Start, error.Span.Length));
    }

    [Fact]
    public void RefIsNotSilentlyDiscardedFromAConstructor()
    {
        var diagnostics = Compiler.Analyze([new SourceFile("ref-constructor.gf", "class S{public ref S(){}}int main()=>0;")], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("ref currently applies to properties and indexers", error.Message);
    }

    [Theory]
    [InlineData("class S{public S(){}}int main()=>0;")]
    [InlineData("class S{public const S(){}}int main()=>0;")]
    [InlineData("struct S{public copy S(readonly(S)* source){}}int main()=>0;")]
    [InlineData("class S{static S(){}}int main()=>0;")]
    public void ExistingConstructorFormsKeepTheirSyntax(string source)
        => Assert.Empty(Compiler.Analyze([new SourceFile("valid-constructor.gf", source)], new() { NoStdLib = true }));

    [Fact]
    public void GenericOwnerUsesItsBareConstructorName()
        => Assert.Equal(42, CompilerTestHelper.Run("struct Box<T>{public T value;public Box(T item){value=item;}}int main(){Box<int> box=new Box<int>(42);return box.value;}").ExitCode);

    [Fact]
    public void InvalidConstructorDoesNotPreventLaterMemberBinding()
    {
        const string source = "class S{public S*(){} public static int Healthy(){int answer=42;return answer;}}int main()=>S::Healthy();";
        var snapshot = new AnalysisSnapshot([new SourceFile("constructor-recovery.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("A constructor declaration requires a plain, unqualified name without type arguments", error.Message);
        var healthy = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<MethodDeclaration>(), method => method.Name == "Healthy");
        var answerUse = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<IdentifierExpression>(), identifier => identifier.Name == "answer");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(answerUse)));
        var call = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<CallExpression>());
        Assert.Same(healthy, snapshot.Checker.GetResolvedCall(call));
    }
}
