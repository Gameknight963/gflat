using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EnumerableToLookupEditorTests
{
    [Fact]
    public void BothGroupMaterializersInferNestedResultTypesAndSelectTheCorrectDeclaration()
    {
        const string text="using std; using std::Collections;int Key(int value)=>value%2;long Square(int value)=>(long)value*(long)value;void Probe(){int[3] data=[2,5,9];Span<int> values=new(data,3);HashMap<int,Vector<int>> originals=values.AsEnumerable().ToLookup(Key);HashMap<int,Vector<long>> squares=values.AsEnumerable().ToLookup(Key,Square);}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="EnumerableToLookup.gf");
        var source=new SourceFile(Path.GetFullPath("to-lookup-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{(Call:".ToLookup(Key)",Arity:1,Result:"HashMap<int,Vector<int>>",Declaration:"ToLookup<T, K : IHashable, S"),
            (Call:".ToLookup(Key,Square)",Arity:2,Result:"HashMap<int,Vector<long>>",Declaration:"ToLookup<T, K : IHashable, V")}){
            int use=text.IndexOf(row.Call,StringComparison.Ordinal)+1;Assert.True(use>0);
            var completion=Json(model.Completion(source.Path,use)).GetProperty("items");
            Assert.Contains(completion.EnumerateArray(),item=>item.GetProperty("label").GetString()=="ToLookup");
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Result,hover.Replace(" ",""));Assert.Contains("ToLookup",hover);Assert.DoesNotContain('$',hover);
            int declaration=library.Text.IndexOf(row.Declaration,StringComparison.Ordinal);Assert.True(declaration>=0);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            int argument=use+"ToLookup(".Length;if(row.Arity==2)argument+="Key,".Length;
            var help=Json(model.SignatureHelp(source.Path,argument));
            Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            Assert.Contains(row.Result,selected.GetProperty("label").GetString()!.Replace(" ",""));
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
