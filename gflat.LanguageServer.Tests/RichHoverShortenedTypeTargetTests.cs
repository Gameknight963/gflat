using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Independent cross-scope navigation repros; no standard library required.
public sealed class RichHoverShortenedTypeTargetTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void CallerShortenedReturnTypeTargetsItsActualDeclarationInsteadOfAMissingOrShadowingDeclarationAlias(bool alias,bool property)
    {
        string library="namespace App{public struct Thing{}}namespace Other{public struct Thing{}}"+
            "namespace Library{"+(alias?"alias Thing=Other::Thing;":"")+
            (property?"public App::Thing Value{get=>default;}":"public App::Thing Make()=>default;")+"}";
        string member=property?"Value":"Make";
        string caller="using App;void Use(){"+(property?"App::Thing kept=Library::Value;":"Library::Make();")+"}";
        var (model,source,extra)=Analyze(caller,library);
        int use=caller.IndexOf(member,StringComparison.Ordinal);
        var hover=Rich(model,source,use);
        string text=hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Thing Library::"+member,text);Assert.DoesNotContain("App::Thing",text);
        Assert.Equal(Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString(),text);
        var run=Assert.Single(Runs(hover),item=>item.GetProperty("Text").GetString()=="Thing");
        Assert.Equal("struct name",run.GetProperty("ClassificationTypeName").GetString());
        Target(model,extra,library.IndexOf("Thing{",StringComparison.Ordinal),run);
    }

    [Fact]
    public void SameSpellingGenericArgumentsPreserveSeparateShortenedTypeAndDeclaredAliasTargets()
    {
        const string library="namespace App{public struct Thing{}public struct Another{}}namespace Other{public struct Thing{}}"+
            "public struct Pair<A,B>{}namespace Library{alias Thing=Other::Thing;public Pair<App::Thing,Thing> Mixed()=>default;public Pair<App::Thing,App::Another> Two()=>default;}";
        const string caller="using App;void Use(){Library::Mixed();Library::Two();}";
        var (model,source,extra)=Analyze(caller,library);
        var hover=Rich(model,source,caller.IndexOf("Mixed",StringComparison.Ordinal));
        Assert.Contains("Pair<Thing, Thing>",hover.GetProperty("contents").GetProperty("value").GetString());
        var names=Runs(hover).Where(item=>item.GetProperty("Text").GetString()=="Thing").ToArray();Assert.Equal(2,names.Length);
        Assert.Equal("struct name",names[0].GetProperty("ClassificationTypeName").GetString());
        Target(model,extra,library.IndexOf("Thing{",StringComparison.Ordinal),names[0]);
        Assert.Equal("type",names[1].GetProperty("ClassificationTypeName").GetString());
        Target(model,extra,library.IndexOf("Thing=",StringComparison.Ordinal),names[1]);
        // A second shortened token must account for characters removed earlier.
        var two=Rich(model,source,caller.IndexOf("Two",StringComparison.Ordinal));
        Assert.Contains("Pair<Thing, Another>",two.GetProperty("contents").GetProperty("value").GetString());
        foreach(string name in new[]{"Thing","Another"}){
            var run=Assert.Single(Runs(two),item=>item.GetProperty("Text").GetString()==name);
            Assert.Equal("struct name",run.GetProperty("ClassificationTypeName").GetString());
            Target(model,extra,library.IndexOf(name+"{",StringComparison.Ordinal),run);
        }
    }

    [Fact]
    public void AmbiguousCallerImportsRemainQualifiedAndOpenGenericParameterTargetsRemainDeclarative()
    {
        const string library="namespace App{public struct Thing{}}namespace Other{public struct Thing{}}"+
            "namespace Library{public App::Thing Qualified()=>default;public Thing Identity<Thing>(Thing value)=>value;}";
        const string caller="using App;using Other;void Use(){Library::Qualified();}";
        var (model,source,extra)=Analyze(caller,library);
        var qualified=Rich(model,source,caller.IndexOf("Qualified",StringComparison.Ordinal));
        Assert.Contains("App::Thing",qualified.GetProperty("contents").GetProperty("value").GetString());
        var concrete=Assert.Single(Runs(qualified),item=>item.GetProperty("Text").GetString()=="Thing");
        Assert.Equal("struct name",concrete.GetProperty("ClassificationTypeName").GetString());
        Target(model,extra,library.IndexOf("Thing{",StringComparison.Ordinal),concrete);
        var generic=Rich(model,extra,library.IndexOf("Identity",StringComparison.Ordinal));
        var parameters=Runs(generic).Where(item=>item.GetProperty("Text").GetString()=="Thing").ToArray();Assert.Equal(3,parameters.Length);
        foreach(var parameter in parameters)Assert.Equal("type parameter name",parameter.GetProperty("ClassificationTypeName").GetString());
        // The return/parameter TYPE uses resolve their declaration parameter.
        // The generic-header name has its own existing rendering path.
        foreach(var parameter in new[]{parameters[0],parameters[2]})
            Target(model,extra,library.IndexOf("<Thing>",StringComparison.Ordinal)+1,parameter);
    }

    private static (EditorModel Model,SourceFile Source,SourceFile Extra) Analyze(string caller,string library)
    {
        var source=new SourceFile(Path.GetFullPath("rich-type-caller.gf"),caller);
        var extra=new SourceFile(Path.GetFullPath("rich-type-library.gf"),library);
        SourceFile[] files=[source,extra];var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,extra);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static JsonElement Rich(EditorModel model,SourceFile file,int offset)
        =>Json(model.Hover(file.Path,offset,new VisualStudioHover(Json(new{}))));
    private static IEnumerable<JsonElement> Runs(JsonElement hover)
        =>hover.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements").EnumerateArray()
            .Single(item=>item.GetProperty("_vs_type").GetString()=="ClassifiedTextElement").GetProperty("Runs").EnumerateArray();
    private static void Target(EditorModel model,SourceFile file,int declaration,JsonElement run)
    {
        var expected=model.Definition(file.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(Json(expected).GetRawText(),run.GetProperty("_gflat_target").GetRawText());
    }
}
