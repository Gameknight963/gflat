using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Independent repro: no std dependency, two distinct outer owners.
public sealed class NestedClosedTypeHoverTests
{
    [Theory]
    [InlineData("struct",false)]
    [InlineData("struct",true)]
    [InlineData("class",false)]
    [InlineData("class",true)]
    public void NestedTypeUsesKeepClosedOuterArgumentsLayoutSourceAndGenericDefinition(string kind,bool reversed)
    {
        string library="namespace Library{public "+kind+" Owner<T>{public struct Nested{public T Value;}}}"+
            "namespace Other{public "+kind+" Owner<T>{public struct Nested{public T Value;}}}";
        string first=reversed?"long":"int",second=reversed?"int":"long";
        string text="int Probe(){Library::Owner<"+first+">::Nested first=default;"+
            "Library::Owner<"+second+">::Nested second=default;Other::Owner<byte>::Nested other=default;return 0;}";
        var extra=new SourceFile(Path.GetFullPath("nested-closed-library.gf"),library);
        var source=new SourceFile(Path.GetFullPath("nested-closed-main.gf"),text);
        SourceFile[] files=[extra,source];var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int sourceDefinition=library.IndexOf("Nested{",StringComparison.Ordinal);
        var expected=model.Definition(extra.Path,sourceDefinition);Assert.NotNull(expected);
        foreach(var row in new[]{(Type:first,Variable:"first",Size:first=="int"?4:8),(Type:second,Variable:"second",Size:second=="int"?4:8)})
        {
            int offset=text.IndexOf("Nested "+row.Variable,StringComparison.Ordinal);
            string hover=Hover(model,source,offset);
            Assert.Contains("Owner<"+row.Type+">::Nested",hover);Assert.DoesNotContain('$',hover);
            Assert.Contains("**Size:** "+row.Size+" bytes",hover);
            Assert.Equal(expected,model.Definition(source.Path,offset));
        }
        int other=text.IndexOf("Nested other",StringComparison.Ordinal);
        string otherHover=Hover(model,source,other);Assert.Contains("Other::Owner<byte>::Nested",otherHover);
        Assert.Contains("**Size:** 1 bytes",otherHover);
        var otherDefinition=model.Definition(extra.Path,library.LastIndexOf("Nested{",StringComparison.Ordinal));
        Assert.NotNull(otherDefinition);Assert.NotEqual(expected,otherDefinition);Assert.Equal(otherDefinition,model.Definition(source.Path,other));
        string definitionHover=Hover(model,extra,sourceDefinition);
        Assert.DoesNotContain("Owner<int>",definitionHover);Assert.DoesNotContain("Owner<long>",definitionHover);
        Assert.Contains("Owner::Nested",definitionHover);
        Assert.DoesNotContain("**Size:**",definitionHover);Assert.DoesNotContain("**Alignment:**",definitionHover);
        var rich=Json(model.Hover(source.Path,text.IndexOf("Nested first",StringComparison.Ordinal),
            new VisualStudioHover(Json(new Dictionary<string,object>{["struct.public"]=new{guid="00000000-0000-0000-0000-000000000001",id=1}}))));
        var icon=rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0];
        Assert.Equal("struct",icon.GetProperty("AutomationName").GetString());
        var runs=rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[1].GetProperty("Runs");
        var nestedRun=Assert.Single(runs.EnumerateArray(),run=>run.GetProperty("Text").GetString()=="Nested");
        Assert.Equal("struct name",nestedRun.GetProperty("ClassificationTypeName").GetString());
        Assert.Equal(Json(expected).GetRawText(),nestedRun.GetProperty("_gflat_target").GetRawText());
    }

    [Fact]
    public void MultipleOuterLevelsAndOwnGenericArgumentsKeepTheFinalSourceNameClickable()
    {
        const string library="namespace Library{public struct Outer<T>{public struct Middle<U>{public struct Leaf<V>{public T A;public U B;public V C;}}}}";
        const string text="int Probe(){Library::Outer<byte>::Middle<ushort>::Leaf<ulong> value=default;return 0;}";
        var extra=new SourceFile(Path.GetFullPath("deep-nested-library.gf"),library);
        var source=new SourceFile(Path.GetFullPath("deep-nested-main.gf"),text);
        SourceFile[] files=[extra,source];var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);int use=text.IndexOf("Leaf<",StringComparison.Ordinal);
        string hover=Hover(model,source,use);
        Assert.Contains("Outer<byte>::Middle<ushort>::Leaf<ulong>",hover);Assert.DoesNotContain('$',hover);
        Assert.Contains("**Size:** 16 bytes",hover);
        int declaration=library.IndexOf("Leaf<V>",StringComparison.Ordinal);
        var expected=model.Definition(extra.Path,declaration);Assert.NotNull(expected);Assert.Equal(expected,model.Definition(source.Path,use));
        string open=Hover(model,extra,declaration);
        Assert.DoesNotContain("**Size:**",open);Assert.DoesNotContain("**Alignment:**",open);Assert.Contains("Leaf<V>",open);
        var rich=Json(model.Hover(source.Path,use,new VisualStudioHover(Json(new{}))));
        var runs=rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs");
        var leaf=Assert.Single(runs.EnumerateArray(),run=>run.GetProperty("Text").GetString()=="Leaf");
        Assert.Equal("struct name",leaf.GetProperty("ClassificationTypeName").GetString());
        Assert.Equal(Json(expected).GetRawText(),leaf.GetProperty("_gflat_target").GetRawText());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile file,int offset)
        =>Json(model.Hover(file.Path,offset,options:new HoverOptions(ShowTypeLayout:true))).GetProperty("contents").GetProperty("value").GetString()!;
}
