using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DoubleKeywordHoverTests
{
    [Theory]
    [InlineData("do|uble Read(double value)=>value;int main()=>0;")]
    [InlineData("double Read(do|uble value)=>value;int main()=>0;")]
    [InlineData("int main()=>(int)sizeof(do|uble);")]
    public void PrimitiveKeywordKeepsItsHoverLayoutWithoutADeclarationTarget(string marked)
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("double-keyword-hover.gf"), marked.Remove(offset, 1));
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
        string markdown = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("```gflat\ndouble\n```", markdown);
        Assert.Contains("**Size:** 8 bytes", markdown);
        Assert.Contains("**Alignment:** 8 bytes", markdown);
        Assert.Null(model.Definition(source.Path, offset));
    }
}
