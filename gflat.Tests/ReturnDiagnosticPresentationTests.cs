using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ReturnDiagnosticPresentationTests
{
    [Theory]
    // Expression-bodied functions currently locate their synthesized return at the arrow token.
    [InlineData("namespace Lib{struct Box<T>{public T value;}} Lib::Box<int> Read()=>1;", "Cannot return 'int' from function returning 'Lib::Box<int>'", "=>")]
    [InlineData("namespace Lib{struct Box<T>{public T value;}} int Read(){Lib::Box<int> value=new Lib::Box<int>();return value;}", "Cannot return 'Lib::Box<int>' from function returning 'int'", "return value;")]
    [InlineData("namespace Lib{struct Family<T>{public struct Item{public T value;}}} readonly(Lib::Family<int>::Item) Read(){return;}", "Cannot return void from function returning 'readonly(Lib::Family<int>::Item)'", "return;")]
    [InlineData("namespace Lib{struct Box<T>{public T value;public static Box<T> operator +(Box<T> a,Box<T> b){return 1;}}} int main(){Lib::Box<int> value=new Lib::Box<int>();return 0;}", "Cannot return 'int' from operator returning 'Lib::Box<int>'", "1")]
    [InlineData("namespace Lib{struct Box<T>{public T value;public static Box<T> operator +(Box<T> a,Box<T> b){return;}}} int main(){Lib::Box<int> value=new Lib::Box<int>();return 0;}", "Cannot return void from operator returning 'Lib::Box<int>'", "return;")]
    public void ReturnFailuresUseSourceNamesAndKeepTheirOriginalSpans(string source, string expected, string offending)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("return-presentation.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.Equal("return-presentation.gf", error.FilePath);
        Assert.Equal(offending, source.Substring(error.Span.Start, error.Span.Length));
    }
}
