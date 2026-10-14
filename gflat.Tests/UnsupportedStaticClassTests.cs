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
}
