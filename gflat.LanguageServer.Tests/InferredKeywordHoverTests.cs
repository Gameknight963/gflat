using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class InferredKeywordHoverTests
{
    [Theory]
    [InlineData("void Probe(){var number=7;}","var number","int")]
    [InlineData("struct Box<T>{public T Value;public Box(T value){Value=value;}}void Probe(){var box=new Box<int>(7);}","var box","Box<int>")]
    [InlineData("void Probe(readonly(int)*? input){var value=input;}","var value","readonly(int)*?")]
    [InlineData("void Probe(){var values=[1,2,3];}","var values","int[3]")]
    [InlineData("void Probe(){for(var index=0;index<1;index++){}}","var index","int")]
    public void KeywordDisplaysOnlyTheRecordedTypeAndItsOwnRange(string text,string marker,string expected)
    {
        var (model,source,snapshot)=Analyze(text);Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        int start=text.IndexOf(marker,StringComparison.Ordinal);Assert.True(start>=0);
        var hover=Json(model.Hover(source.Path,start+1,options:new(){ShowTypeLayout=false}));
        Assert.Equal("```gflat\n"+expected+"\n```",hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Equal(start,hover.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(start+3,hover.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void InferredUserTypeUsesExistingRichNavigationAndLayoutOptions()
    {
        const string libraryText="namespace Types{public struct Box<T>{public T Value;public Box(T value){Value=value;}}}";
        const string text="using Types;void Probe(){var box=new Box<int>(7);int value=box.Value;}";
        var library=new SourceFile(Path.GetFullPath("inferred-keyword-library.gf"),libraryText);
        var (model,source,snapshot)=Analyze(text,library);Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        int start=text.IndexOf("var box",StringComparison.Ordinal);
        var hover=Json(model.Hover(source.Path,start,new VisualStudioHover(Json(new{}))));
        string markdown=hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("```gflat\nBox<int>\n```",markdown);Assert.Contains("**Size:** 4 bytes",markdown);
        var header=hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements");
        var runs=header.EnumerateArray().Single(item=>item.GetProperty("_vs_type").GetString()=="ClassifiedTextElement").GetProperty("Runs").EnumerateArray().ToArray();
        Assert.Equal("Box<int>",string.Concat(runs.Select(run=>run.GetProperty("Text").GetString())));
        var box=Assert.Single(runs,run=>run.GetProperty("Text").GetString()=="Box");
        Assert.Equal("struct name",box.GetProperty("ClassificationTypeName").GetString());
        var target=box.GetProperty("_gflat_target");Assert.Contains("inferred-keyword-library.gf",target.GetProperty("uri").GetString());
        Assert.Equal(libraryText.IndexOf("Box<T>",StringComparison.Ordinal),target.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        string noLayout=Json(model.Hover(source.Path,start,options:new(){ShowTypeLayout=false})).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.DoesNotContain("**Size:**",noLayout);Assert.DoesNotContain("**Alignment:**",noLayout);
        int identifier=text.LastIndexOf("box.Value",StringComparison.Ordinal);
        Assert.Contains("Box<int> box",Json(model.Hover(source.Path,identifier)).GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("new()")]
    [InlineData("null")]
    public void FailedInferenceHasNoInventedTypeAndHealthyFollowingDeclarationsStillWork(string initializer)
    {
        string text="void Broken(){var broken="+initializer+";}void Healthy(){var healthy=7;int value=healthy;}";
        var (model,source,snapshot)=Analyze(text);Assert.Contains(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        Assert.Null(model.Hover(source.Path,text.IndexOf("var broken",StringComparison.Ordinal)));
        string healthy=Json(model.Hover(source.Path,text.IndexOf("var healthy",StringComparison.Ordinal))).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("```gflat\nint\n```",healthy);Assert.DoesNotContain("**Size:**",healthy);
    }

    [Fact]
    public void AVariableNamedVarKeepsItsOrdinarySymbolHover()
    {
        const string text="int Probe(){int var=7;return var;}";
        var (model,source,snapshot)=Analyze(text);Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        int use=text.LastIndexOf("var",StringComparison.Ordinal);
        Assert.Contains("int var",Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path,use));
    }

    private static (EditorModel Model,SourceFile Source,AnalysisSnapshot Snapshot) Analyze(string text,SourceFile? library=null)
    {
        var source=new SourceFile(Path.GetFullPath("inferred-keyword.gf"),text);
        SourceFile[] files=library==null?[source]:[library,source];
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});return(new(snapshot,files),source,snapshot);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
