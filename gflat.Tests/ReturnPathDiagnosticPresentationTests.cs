using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ReturnPathDiagnosticPresentationTests
{
    [Theory]
    [InlineData("struct Entry{public int Value{get{}}}", "property getter 'Value'", "get{}")]
    [InlineData("struct Entry{public int this[int index]{get{}}}", "indexer getter", "get{}")]
    [InlineData("namespace Lib{int F<T>(){}} int main()=>Lib::F<int>();", "function 'Lib::F<int>'", "int F<T>(){}")]
    [InlineData("int Read(){}", "function 'Read'", "int Read(){}")]
    [InlineData("namespace Lib{int Read(){}}", "function 'Read'", "int Read(){}")]
    [InlineData("struct Entry{public int Read(){}}", "function 'Read'", "public int Read(){}")]
    public void ReturnPathErrorsUseSourceDeclarations(string source, string declaration, string offending)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("return-path-presentation.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Not all code paths return a value in " + declaration, error.Message);
        Assert.Equal("return-path-presentation.gf", error.FilePath);
        Assert.Equal(offending, source.Substring(error.Span.Start, error.Span.Length));
    }
}
