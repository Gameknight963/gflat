using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class UnsupportedStaticClassTests
{
    [Theory]
    [InlineData("public static class Broken {}")]
    [InlineData("abstract static class Broken {}")]
    [InlineData("namespace N { public static class Broken {} }")]
    public void UnsupportedStaticClassesHaveSourceDiagnostics(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit([new SourceFile("static.gf", declaration + " int Good() => 42;")]));
        Assert.Contains("Static classes", error.Message);
        Assert.Equal("static.gf", error.Span.Source!.Path);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("enum")]
    public void NestedStaticTypeModifiersAreNotDiscarded(string kind)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit([new SourceFile("static.gf", "class Outer { public static " + kind + " Broken {} } int Good() => 42;")]));
        Assert.Contains("Static type declarations", error.Message);
        Assert.Equal("static.gf", error.Span.Source!.Path);
    }
}
