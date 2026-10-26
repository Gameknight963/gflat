using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DefaultAlgorithmContractEditorTests
{
    [Fact]
    public void OrderingDefaultsRetainActualInferredIntAndTheirOwnDeclarations()
        =>Check("ordering-contract-editor.gf","default_ordering.gf","IComparable<T>",
            "nuint minimum=MinIndex(values.AsImmutable());nuint maximum=MaxIndex(values.AsImmutable());int order=SequenceCompare(values.AsImmutable(),values.AsImmutable());",
            [("MinIndex","nuint",1),("MaxIndex","nuint",1),("SequenceCompare","int",2)]);

    [Fact]
    public void EqualityDefaultsRetainActualInferredIntAndTheirOwnDeclarations()
        =>Check("equality-contract-editor.gf","default_equality.gf","IEquatable<T>",
            "bool same=SequenceEqual(values.AsImmutable(),values.AsImmutable());nuint first=IndexOf(values.AsImmutable(),&key);nuint last=LastIndexOf(values.AsImmutable(),&key);bool found=Contains(values.AsImmutable(),&key);",
            [("SequenceEqual","bool",2),("IndexOf","nuint",2),("LastIndexOf","nuint",2),("Contains","bool",2)]);

    private static void Check(string path,string fileName,string contract,string calls,(string Name,string Return,int Arity)[] rows)
    {
        string text="using std; using std::Memory; using std::Collections;void Probe(){int[4] data=[1,2,2,4];Span<int> values=new(data,4);int key=2;"+calls+"}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(file=>new SourceFile(file,File.ReadAllText(file))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)==fileName);
        var source=new SourceFile(Path.GetFullPath(path),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in rows){
            int use=text.IndexOf(row.Name+"(",StringComparison.Ordinal);Assert.True(use>=0);
            Assert.Contains(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Name+"<int>",hover);Assert.Contains(row.Return,hover);Assert.DoesNotContain('$',hover);
            string anchor="public "+row.Return+" "+row.Name+"<T : "+contract+">(";
            int declaration=library.Text.IndexOf(anchor,StringComparison.Ordinal);Assert.True(declaration>=0);
            var definition=model.Definition(library.Path,declaration+anchor.IndexOf(row.Name+"<",StringComparison.Ordinal));
            Assert.NotNull(definition);Assert.Equal(definition,model.Definition(source.Path,use));
            int argument=use+row.Name.Length+1;if(row.Arity==2)argument+="values.AsImmutable(),".Length;
            var help=Json(model.SignatureHelp(source.Path,argument));Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var signatures=help.GetProperty("signatures");var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;Assert.Contains("Span<readonly(int)>",label);
            // Free-call families also contain the existing explicit comparator and query overloads.
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("parameters").GetArrayLength()==row.Arity+1);
        }
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
