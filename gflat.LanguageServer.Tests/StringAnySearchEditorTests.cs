using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise both overload families from the actual String declaration.
public sealed class StringAnySearchEditorTests
{
    [Theory]
    [InlineData("IndexOfAny",3)]
    [InlineData("LastIndexOfAny",2)]
    public void AnyByteSearchOffersBothOverloadsAndTheirConcreteImmutableSignatures(string member,int countedArguments)
    {
        string arguments=countedArguments==3?"\"xy\",2,1":"\"xy\",2";
        string text="using std; using std::Text;void Observe(immutable(String)* text){nuint counted=text."+member+"("+arguments+");nuint terminated=text."+member+"(\"xy\");}";
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Text/ByteSearch.gf","Text/String.gf"];
        var files=names.Select(name=>Path.Combine(root,name)).Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("string-any-search-editor.gf"),text);files.Add(source);
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="String.gf");
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        int counted=text.IndexOf(member+"(",StringComparison.Ordinal),terminated=text.LastIndexOf(member+"(",StringComparison.Ordinal);
        Assert.Single(Json(model.Completion(source.Path,counted)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==member);
        foreach(var row in new[]{(Use:counted,Arguments:countedArguments,Source:member+"(readonly(char)* values, nuint count"),
            (Use:terminated,Arguments:1,Source:member+"(readonly(char)* values)")})
        {
            string hover=Json(model.Hover(source.Path,row.Use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(member,hover);Assert.Contains("nuint",hover);Assert.Contains("immutable",hover);Assert.Contains("values",hover);Assert.DoesNotContain('$',hover);
            if(member=="LastIndexOfAny")Assert.Contains("nothrow",hover);
            int declaration=library.Text.IndexOf(row.Source,StringComparison.Ordinal);Assert.True(declaration>=0,"Missing source "+row.Source);
            var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,row.Use));Assert.Contains("String.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
            int start=text.IndexOf('(',row.Use)+1;
            int end=text.IndexOf(')',start);
            int argument=row.Arguments==1?start+1:text.LastIndexOf(',',end)+1;
            var help=Json(model.SignatureHelp(source.Path,argument));
            var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            Assert.Equal(row.Arguments-1,help.GetProperty("activeParameter").GetInt32());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arguments,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;
            Assert.Contains(member,label);Assert.Contains("nuint",label);Assert.Contains("values",label);Assert.DoesNotContain('$',label);
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("parameters").GetArrayLength()==1);
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("parameters").GetArrayLength()==countedArguments);
            if(row.Arguments==3)Assert.Contains("start",label);
        }
        Assert.NotEqual(model.Definition(source.Path,counted),model.Definition(source.Path,terminated));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
