using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Loads the actual optional owning codec and dependencies.
public sealed class Base32TextEditorTests
{
    [Theory]
    [InlineData("Encode","f",1)]
    [InlineData("Decode","MY======",8)]
    public void OwningBase32MethodsExposeReadableStringResultsAndSourceServices(string member,string input,int count)
    {
        string text="using std;int Probe(){String result=Base32Text::"+member+"(\""+input+"\","+count+");return (int)result.Length;}";
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf",
            "ByteSearch.gf","String.gf","Base32.gf","Base32Text.gf"];
        var sources=names.Select(name=>Path.Combine(root,name))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("base32-text-editor.gf"),text);sources.Add(source);
        var snapshot=new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int call=text.IndexOf(member+"(",StringComparison.Ordinal);
        var completion=Json(model.Completion(source.Path,call));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==member);
        string hover=Hover(model,source,call);
        Assert.Contains("String",hover);Assert.Contains(member+"(",hover);Assert.Contains("nuint count",hover);
        Assert.DoesNotContain('$',hover);
        SourceFile library=sources.Single(file=>Path.GetFileName(file.Path)=="Base32Text.gf");
        int declaration=library.Text.IndexOf(member+"(",StringComparison.Ordinal);
        Assert.True(declaration>=0);
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,call));
        Assert.Contains("Base32Text.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        int secondArgument=text.IndexOf(',' ,call)+1;
        var help=Json(model.SignatureHelp(source.Path,secondArgument));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("String",label);Assert.Contains(member+"(",label);Assert.Contains("nuint count",label);
        Assert.DoesNotContain('$',label);Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        Assert.Equal(2,signature.GetProperty("parameters").GetArrayLength());
        int use=text.LastIndexOf("result",StringComparison.Ordinal);
        Assert.Contains("String result",Hover(model,source,use));
        var resultDefinition=model.Definition(source.Path,text.IndexOf("result=",StringComparison.Ordinal));
        Assert.NotNull(resultDefinition);Assert.Equal(resultDefinition,model.Definition(source.Path,use));
        int type=text.IndexOf("String",StringComparison.Ordinal);
        Assert.Equal("struct",Color(model,source,type));
    }

    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);

    // The consumer is one line, matching StandardCollectionEditorTests' decoder.
    private static string Color(EditorModel model,SourceFile source,int offset)
    {
        int[] data=Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();
        int column=0;
        for(int i=0;i<data.Length;i+=5){
            column=data[i]==0?column+data[i+1]:data[i+1];
            if(column==offset)return EditorModel.TokenTypes[data[i+3]];
        }
        Assert.Fail("Missing String type semantic token");return "";
    }
}
