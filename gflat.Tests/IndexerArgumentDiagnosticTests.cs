using gflat.diagnostics;

namespace gflat.Tests;

public sealed class IndexerArgumentDiagnosticTests
{
    [Theory]
    [InlineData("class", false)]
    [InlineData("struct", false)]
    [InlineData("class", true)]
    [InlineData("struct", true)]
    public void GetterArgumentErrorsDescribeTheSourceIndexer(string kind, bool wrongType)
    {
        string read = wrongType ? "entry[\"x\"]" : "entry[1,2]";
        string source = "namespace Lib{" + kind + " Entry<T>{public int this[int index]{get{return index;}}}}" +
            "int main(){Lib::Entry<int> entry=new Lib::Entry<int>();return " + read + ";}";
        AssertError(source, wrongType
            ? "Argument 1 of indexer accessor 'this[]': cannot pass 'readonly(char)*' as 'int'"
            : "Indexer accessor 'this[]' expects 1 arguments but got 2", wrongType ? "\"x\"" : read);
    }

    [Theory]
    [InlineData("int Read(int value)=>value;int main()=>Read(1,2);", "Function 'Read' expects 1 arguments but got 2", "Read(1,2)")]
    [InlineData("int Read(int value)=>value;int main()=>Read(\"x\");", "Argument 1 of 'Read': cannot pass 'readonly(char)*' as 'int'", "\"x\"")]
    public void OrdinaryFunctionArgumentErrorsKeepTheirWording(string source, string expected, string offending)
        => AssertError(source, expected, offending);

    private static void AssertError(string source, string expected, string offending)
    {
        var errors = Compiler.Analyze([new SourceFile("indexer-argument-diagnostics.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(errors, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.Equal("indexer-argument-diagnostics.gf", error.FilePath);
        Assert.Equal(offending, source.Substring(error.Span.Start, error.Span.Length));
    }
}
