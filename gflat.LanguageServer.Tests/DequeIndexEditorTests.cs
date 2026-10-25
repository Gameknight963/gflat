using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DequeIndexEditorTests
{
    [Fact]
    public void IndexedMutationsHaveClosedElementTypesAndNavigateToTheirPublicDeclarations()
    {
        const string text="using std;void Probe(){Deque<int> values=new();values.AddLast(1);values.Insert(0,2);int removed=values.RemoveAt(1);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="Deque.gf");
        var source=new SourceFile(Path.GetFullPath("deque-index-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{(Name:"Insert",Arity:2,Declaration:"public void Insert(",Result:"void"),
            (Name:"RemoveAt",Arity:1,Declaration:"public T RemoveAt(",Result:"int")}){
            int use=text.IndexOf("."+row.Name+"(",StringComparison.Ordinal)+1;Assert.True(use>0);
            var completion=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Contains(completion.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Result,hover);Assert.Contains("Deque<int>",hover);Assert.Contains(row.Name,hover);Assert.DoesNotContain('$',hover);
            int declarationStart=library.Text.IndexOf(row.Declaration,StringComparison.Ordinal);Assert.True(declarationStart>=0);
            int declaration=declarationStart+row.Declaration.IndexOf(row.Name,StringComparison.Ordinal);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            int argument=use+row.Name.Length+1;if(row.Arity==2)argument+="0,".Length;
            var help=Json(model.SignatureHelp(source.Path,argument));
            Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var selected=Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;
            Assert.Contains("nuint index",label);
            if(row.Arity==2)Assert.Contains("int value",label);else Assert.Contains("int std::Deque<int>::RemoveAt",label);
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
