using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class GenericFunctionExpansionTests
{
    [Theory]
    [InlineData("void F<T>() { F<T*>(); } int main(){F<int>();return 0;}")]
    [InlineData("void F<T>() { G<T*>(); } void G<T>() { F<T*>(); } int main(){F<int>();return 0;}")]
    [InlineData("struct Box<T>{ public Box(){F<T*>();} } void F<T>(){Box<T> item=new Box<T>();} int main(){F<int>();return 0;}")]
    [InlineData("struct Box<T>{ public Box(){F<T*>();} } void F<T>(){Box<T> item=new Box<T>();} int main(){Box<int> first=new Box<int>();return 0;}")]
    public void ExpandingFunctionAndTypeChainsHaveBoundedDiagnostics(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains("specialization exceeds the compiler nesting limit of 64", error.Description);
        Assert.True(error.Line > 0);
    }

    [Theory]
    [InlineData("void F<T>() { F<T>(); }")]
    [InlineData("void F<T>() { G<T>(); } void G<T>() { F<T>(); }")]
    public void OrdinaryRecursiveSpecializationsReusePublishedFunctions(string declarations)
        => Compiler.Check(declarations + " int main(){F<int>();return 0;}", options: new() { NoStdLib = true });

    [Fact]
    public void LimitCountsActiveWorkRatherThanSequentialFunctions()
    {
        var source = new System.Text.StringBuilder("void F<T>() {} int main(){");
        for (int i = 1; i <= 80; i++) source.Append($"F<int[{i}]>();");
        Compiler.Check(source + "return 0;}", options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("void F<T>() { F<T*>(); }", "F<int>();")]
    [InlineData("struct Box<T>{ public Box(){F<T*>();} } void F<T>(){Box<T> item=new Box<T>();}", "F<int>();")]
    [InlineData("struct Box<T>{ public Box(){F<T*>();} } void F<T>(){Box<T> item=new Box<T>();}", "Box<int> first=new Box<int>();")]
    public void RecoveryRejectsIncompleteCachedFunctionsAndChecksUnrelatedGenerics(string declarations, string call)
    {
        string source = declarations + $$"""
            void Bad() { {{call}} }
            void Again() { {{call}} }
            int Good<T>(T value) => 7;
            int Healthy() => Good<int>(3);
            """;
        var diagnostics = new DiagnosticBag();
        var compilation = new Compilation([new SourceFile("expansion.gf", source)], diagnostics,
            options: new() { NoStdLib = true });
        var checker = new TypeChecker(diagnostics, recoverErrors: true);
        compilation.Unit.Accept(checker);
        Assert.True(diagnostics.Items.Count(d => d.Message.Contains("compiler nesting limit")) >= 2);
        Assert.DoesNotContain(diagnostics.Items, d => d.Span.Start >= source.IndexOf("int Good", StringComparison.Ordinal));
        Assert.Contains(compilation.Unit.Members.OfType<MethodDeclaration>(), method => method.Name == "Good$int");
    }
}
