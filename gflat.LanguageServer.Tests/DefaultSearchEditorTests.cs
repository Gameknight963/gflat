using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DefaultSearchEditorTests
{
    [Fact]
    public void ProtectedSpanDefaultsUseActualInferredArgumentsAndTheirOwnDeclarations()
    {
        const string text="using std; using std::Memory; using std::Collections;void Probe(){int[5] data=[1,2,2,4,9];Span<int> values=new(data,5);int key=2;nuint lower=LowerBound(values.AsImmutable(),&key);nuint upper=UpperBound(values.AsImmutable(),&key);nuint match=BinarySearch(values.AsImmutable(),&key);bool sorted=IsSorted(values.AsImmutable());}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="default_ordering.gf");
        var source=new SourceFile(Path.GetFullPath("default-search-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{(Name:"LowerBound",Arity:2,Return:"nuint"),(Name:"UpperBound",Arity:2,Return:"nuint"),(Name:"BinarySearch",Arity:2,Return:"nuint"),(Name:"IsSorted",Arity:1,Return:"bool")}){
            int use=text.IndexOf(row.Name+"(",StringComparison.Ordinal);Assert.True(use>=0);
            var items=Json(model.Completion(source.Path,use)).GetProperty("items");Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Name+"<int>",hover);Assert.Contains(row.Return,hover);Assert.DoesNotContain('$',hover);
            string anchor="public "+row.Return+" "+row.Name+"<T : IComparable<T>>(";int start=library.Text.IndexOf(anchor,StringComparison.Ordinal);Assert.True(start>=0);
            var expected=model.Definition(library.Path,start+anchor.IndexOf(row.Name+"<",StringComparison.Ordinal));Assert.NotNull(expected);Assert.Equal(expected,model.Definition(source.Path,use));
            int argument=use+row.Name.Length+1;if(row.Arity==2)argument+="values.AsImmutable(),".Length;
            var help=Json(model.SignatureHelp(source.Path,argument));Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());string label=selected.GetProperty("label").GetString()!;
            Assert.Contains("Span<readonly(int)>",label);if(row.Arity==2)Assert.Contains("key",label);
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
