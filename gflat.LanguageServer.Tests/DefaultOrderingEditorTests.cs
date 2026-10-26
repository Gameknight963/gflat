using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DefaultOrderingEditorTests
{
    [Fact]
    public void DefaultAndExplicitSortingOverloadsNavigateWithoutHidingTheirSpanArguments()
    {
        const string text="using std; using std::Memory; using std::Collections;int Desc(immutable(int)* a,immutable(int)* b){if(*a>*b)return -1;if(*a<*b)return 1;return 0;}void Probe(){int[4] data=[3,1,2,1];Span<int> values=new(data,4);Sort(values);Sort(values,Desc);StableSort(values);StableSort(values,Desc);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("default-ordering-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{
            (Call:"Sort(values)",Name:"Sort",Arity:1,File:"default_ordering.gf",Anchor:"public void Sort<T : IComparable<T>>("),
            (Call:"Sort(values,Desc)",Name:"Sort",Arity:2,File:"span_algorithms.gf",Anchor:"public void Sort<T>("),
            (Call:"StableSort(values)",Name:"StableSort",Arity:1,File:"default_ordering.gf",Anchor:"public void StableSort<T : IComparable<T>>("),
            (Call:"StableSort(values,Desc)",Name:"StableSort",Arity:2,File:"stable_sort.gf",Anchor:"public void StableSort<T>(")}){
            int use=text.IndexOf(row.Call,StringComparison.Ordinal);Assert.True(use>=0);
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Name+"<int>",hover);Assert.DoesNotContain('$',hover);
            var library=files.Single(file=>Path.GetFileName(file.Path)==row.File);int start=library.Text.IndexOf(row.Anchor,StringComparison.Ordinal);Assert.True(start>=0);
            int declaration=start+row.Anchor.IndexOf(row.Name+"<",StringComparison.Ordinal);var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            int argument=use+row.Name.Length+1;if(row.Arity==2)argument+="values,".Length;
            var help=Json(model.SignatureHelp(source.Path,argument));Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());Assert.Contains("Span<int> values",selected.GetProperty("label").GetString());
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
