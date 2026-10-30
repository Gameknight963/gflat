using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class MissingMemberQualifierDiagnosticTests
{
    [Theory]
    [InlineData("struct", "readonly(Box<int>)", "readonly(Box<int>)")]
    [InlineData("struct", "immutable(Box<int>)", "immutable(Box<int>)")]
    [InlineData("class", "readonly(Box<int>)", "readonly(Box<int>)")]
    [InlineData("class", "immutable(Box<int>)", "immutable(Box<int>)")]
    [InlineData("struct", "Box<int>", "Box<int>")]
    [InlineData("class", "Box<int>", "Box<int>")]
    public void MissingMemberNamesTheResolvedReceiverView(string kind, string receiver, string expected)
    {
        string source = $$"""
            {{kind}} Box<T> { public T* data; }
            void Probe({{receiver}}* value) { value.Missing; }
            """;
        var error = Assert.Throws<TypeCheckException>(() =>
            Compiler.Check(source, options: new() { NoStdLib = true }));
        string prefix = kind == "class" ? "Class " : "";
        Assert.Equal(prefix + "'" + expected + "' has no field or method 'Missing'", error.Description);
    }
}
