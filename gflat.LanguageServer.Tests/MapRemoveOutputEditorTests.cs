using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Uses the actual standard map removal overloads.
public sealed class MapRemoveOutputEditorTests
{
    [Theory]
    [InlineData("HashMap")]
    [InlineData("SortedMap")]
    public void RemoveOutputSelectsItsRefSignatureAndSourceWhileExistingRemoveRemainsDistinct(string map)
    {
        string construction=map=="SortedMap"?"new(Compare)":"new()";
        string text="""
            using std;
            int Compare(immutable(int)* left,immutable(int)* right){if(*left<*right)return -1;if(*left>*right)return 1;return 0;}
            """+"int Probe(){"+map+"<int,int> values="+construction+";values.Add(3,42);"+
            "int key=3;int output=99;bool removed=values.Remove(&key,ref output);bool old=values.Remove(&key);return output;}";
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["IHashable.gf","IEquatable.gf","Hashing.gf","Collections/IEnumerable.gf","Collections/Enumerable.gf",
            "Span.gf","Collections/Vector.gf","Collections/"+map+".gf"];
        var files=names.Select(name=>Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("map-remove-output-editor.gf"),text);files.Add(source);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)==map+".gf");
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int two=text.IndexOf("Remove(&key,ref output)",StringComparison.Ordinal);
        int one=text.IndexOf("Remove(&key)",StringComparison.Ordinal);
        var completion=Json(model.Completion(source.Path,two));
        Assert.Single(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Remove");
        foreach(var row in new[]{(Use:two,Arguments:2,Parameter:1,Source:"Remove(immutable(K)* key, ref V output)"),
            (Use:one,Arguments:1,Parameter:0,Source:"Remove(immutable(K)* key)")})
        {
            string hover=Json(model.Hover(source.Path,row.Use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("bool",hover);Assert.Contains("Remove(",hover);Assert.Contains("readonly(int)* key",hover);Assert.DoesNotContain('$',hover);
            if(row.Arguments==2)Assert.Contains("ref int output",hover);else Assert.DoesNotContain("ref int output",hover);
            int declaration=library.Text.IndexOf(row.Source,StringComparison.Ordinal);Assert.True(declaration>=0,"Missing "+map+" "+row.Source);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,row.Use));
            Assert.Contains(map+".gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
            int argument=row.Arguments==2?text.IndexOf("ref output",row.Use,StringComparison.Ordinal)+4:text.IndexOf("&key",row.Use,StringComparison.Ordinal)+1;
            var help=Json(model.SignatureHelp(source.Path,argument));
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            Assert.Equal(row.Parameter,help.GetProperty("activeParameter").GetInt32());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arguments,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;
            Assert.Contains("bool",label);Assert.Contains("readonly(int)* key",label);Assert.DoesNotContain('$',label);
            if(row.Arguments==2)Assert.Contains("ref int output",label);else Assert.DoesNotContain("ref int output",label);
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("parameters").GetArrayLength()==1);
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("parameters").GetArrayLength()==2);
        }
        Assert.NotEqual(model.Definition(source.Path,one),model.Definition(source.Path,two));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
