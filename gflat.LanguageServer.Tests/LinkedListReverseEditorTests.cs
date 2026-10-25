using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Uses the actual focused standard-library sources.
public sealed class LinkedListReverseEditorTests
{
    [Fact]
    public void MutableReceiverOffersReverseWithItsNoThrowSignatureAndSourceDefinition()
    {
        const string text="using std;void Flip(LinkedList<int>* list) nothrow{list.Reverse();}";
        var (sources,source)=Sources(text);
        var snapshot=new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int call=text.IndexOf("Reverse",StringComparison.Ordinal);
        var completion=Json(model.Completion(source.Path,call));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Reverse");
        string hover=Json(model.Hover(source.Path,call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("void",hover);Assert.Contains("Reverse()",hover);Assert.Contains("nothrow",hover);Assert.DoesNotContain('$',hover);
        SourceFile library=sources.Single(file=>Path.GetFileName(file.Path)=="LinkedList.gf");
        int declaration=library.Text.IndexOf("Reverse()",StringComparison.Ordinal);
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,call));
        Assert.Contains("LinkedList.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        var help=Json(model.SignatureHelp(source.Path,text.IndexOf('(',call)+1));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("void",label);Assert.Contains("Reverse()",label);
    }

    [Theory]
    [InlineData("readonly","non-readonly")]
    [InlineData("immutable","non-immutable")]
    public void ProtectedReceiversRejectMutationWithoutBreakingHealthyMutableCall(string protection,string diagnostic)
    {
        string text="using std;void Invalid("+protection+"(LinkedList<int>)* list){list.Reverse();}\n"+
            "void Healthy(LinkedList<int>* list) nothrow{list.Reverse();}";
        var (sources,source)=Sources(text);
        var snapshot=new AnalysisSnapshot(sources);
        var error=Assert.Single(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        Assert.Equal(source.Path,error.FilePath);Assert.Contains(diagnostic,error.Message);Assert.Contains("Reverse",error.Message);
        int healthy=text.LastIndexOf("Reverse",StringComparison.Ordinal);
        var model=new EditorModel(snapshot,sources);
        Assert.NotNull(model.Definition(source.Path,healthy));
        string hover=Json(model.Hover(source.Path,healthy)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Reverse()",hover);Assert.Contains("nothrow",hover);
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);

    private static (SourceFile[] Sources,SourceFile Source) Sources(string text)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary");
        var files=new[]{"core/Collections/IEnumerable.gf","core/Collections/Enumerable.gf","core/Collections/LinkedList.gf"}
            .Select(relative=>Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("linked-list-reverse-editor.gf"),text);files.Add(source);
        return(files.ToArray(),source);
    }
}
