using gflat.CompileExceptions;

namespace gflat.Tests;

public class NamespaceSyntaxTests
{
    [Fact]
    public void QualifiedAndNestedNamespacesMergeAcrossFiles()
        => Assert.Equal(42, CompilerTestHelper.Run(new[] {
            new SourceFile("one.gf", "namespace A::B { public int First() => 20; }"),
            new SourceFile("two.gf", "namespace A { namespace B { public int Second() => 22; } }"),
            new SourceFile("main.gf", "using A::B; int main() => First() + Second();")
        }).ExitCode);

    [Fact]
    public void FileScopedNamespacesAndUsingsWorkAcrossFiles()
        => Assert.Equal(42, CompilerTestHelper.Run(new[] {
            new SourceFile("one.gf", "namespace A::B; public int Value() => 42;"),
            new SourceFile("two.gf", "namespace App; using A::B; public int Run() => Value();"),
            new SourceFile("main.gf", "int main() => App::Run();")
        }).ExitCode);

    [Fact]
    public void FileScopedNamespaceDoesNotCaptureOtherFiles()
        => Assert.Equal(42, CompilerTestHelper.Run(new[] {
            new SourceFile("one.gf", "namespace A; public int Value() => 1;"),
            new SourceFile("main.gf", "int Value() => 42; int main() => Value();")
        }).ExitCode);

    [Theory]
    [InlineData("namespace A; namespace B;")]
    [InlineData("namespace A { namespace B; }")]
    [InlineData("int F() => 0; namespace A;")]
    [InlineData("namespace A {} namespace B;")]
    [InlineData("namespace A:: {}")]
    public void InvalidNamespaceDeclarationsAreRejected(string code)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(code));
}
