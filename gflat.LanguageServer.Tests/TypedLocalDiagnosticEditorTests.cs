using gflat.ast;

namespace gflat.LanguageServer.Tests;

public sealed class TypedLocalDiagnosticEditorTests
{
    [Fact]
    public void ReservedLocalNameRetainsHealthyLocalAndLaterFunctionServices()
    {
        var source=new SourceFile("reserved-local.gf","int Bad(){int* alias=null;int healthy=42;return healthy;}int Later()=>42;");
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.Contains(snapshot.Diagnostics,error=>error.Descriptor.Id=="GF0005"&&error.Message.Contains("Identifier")&&error.Span.Start==source.Text.IndexOf("alias",StringComparison.Ordinal));
        int use=source.Text.LastIndexOf("healthy",StringComparison.Ordinal);
        var model=new EditorModel(snapshot,[source]);
        Assert.NotNull(model.Hover(source.Path,use));Assert.NotNull(model.Definition(source.Path,use));
        Assert.Contains(snapshot.Nodes.Select(node=>node.Node),node=>node is MethodDeclaration{Name:"Later"});
    }

    [Fact]
    public void ReservedLocalNameInOtherFileRetainsCallResolutionAndNavigation()
    {
        var bad=new SourceFile("a-bad.gf","int Bad(){int* alias=null;return 0;}");
        var good=new SourceFile("b-good.gf","int Healthy(int value)=>value;int Use()=>Healthy(42);");
        var snapshot=new AnalysisSnapshot([bad,good],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Span.SourceId==good.Id);
        var model=new EditorModel(snapshot,[bad,good]);
        int call=good.Text.LastIndexOf("Healthy",StringComparison.Ordinal);
        Assert.NotNull(model.Hover(good.Path,call));Assert.NotNull(model.Definition(good.Path,call));
    }
}
