using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Reads the actual repeated-value sequence and its nested cursor.
public sealed class RepeatedEditorTests
{
    [Fact]
    public void ExplicitRepeatedValueOffersNoThrowMetadataAndItsQueryWrapper()
    {
        const string text="using std; using std::Collections;int Probe(){Repeated<int> values=new(7,3);nuint count=values.Count;bool empty=values.IsEmpty;var query=values.AsEnumerable();return (int)count;}";
        var (model,source,library)=Analyze(text);
        int completionOffset=text.IndexOf("Count;",StringComparison.Ordinal);
        var items=Json(model.Completion(source.Path,completionOffset)).GetProperty("items");
        foreach(string member in new[]{"Count","IsEmpty","GetEnumerator","AsEnumerable"})
            Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==member);
        foreach(var member in new[]{(Name:"Count",Type:"nuint"),(Name:"IsEmpty",Type:"bool")})
        {
            int use=text.IndexOf("values."+member.Name,StringComparison.Ordinal)+"values.".Length;
            string hover=Hover(model,source,use);
            Assert.Contains(member.Name,hover);Assert.Contains(member.Type,hover);Assert.Contains("nothrow",hover);Assert.DoesNotContain('$',hover);
            int declaration=library.Text.IndexOf(member.Name+" {",StringComparison.Ordinal);Assert.True(declaration>=0);
            Definition(model,source,use,library,declaration);
        }
        int query=text.IndexOf("AsEnumerable",StringComparison.Ordinal);
        string queryHover=Hover(model,source,query);Assert.Contains("Enumerable",queryHover);Assert.Contains("int",queryHover);Assert.DoesNotContain('$',queryHover);
        Definition(model,source,query,library,library.Text.IndexOf("AsEnumerable()",StringComparison.Ordinal));
        var signature=Assert.Single(Json(model.SignatureHelp(source.Path,text.IndexOf('(',query)+1)).GetProperty("signatures").EnumerateArray());
        Assert.Contains("AsEnumerable()",signature.GetProperty("label").GetString());
    }

    [Fact]
    public void NestedEnumeratorIsASourceStructWithNoThrowMoveAndConcreteIntCurrent()
    {
        const string text="using std; using std::Collections;int Probe(){Repeated<int> values=new(7,2);Repeated<int>::Enumerator iterator=values.GetEnumerator();bool moved=iterator.MoveNext();int current=iterator.Current;return current;}";
        var (model,source,library)=Analyze(text);
        int nested=text.IndexOf("Enumerator iterator",StringComparison.Ordinal);
        Assert.Equal("struct",Color(model,source,nested));
        string typeHover=Hover(model,source,nested);Assert.Contains("Enumerator",typeHover);Assert.Contains("int",typeHover);Assert.DoesNotContain('$',typeHover);
        int nestedStart=library.Text.IndexOf("struct Enumerator",StringComparison.Ordinal);Assert.True(nestedStart>=0);
        int nestedDeclaration=nestedStart+"struct ".Length;
        Definition(model,source,nested,library,nestedDeclaration);
        int factory=text.IndexOf("GetEnumerator",StringComparison.Ordinal);
        string factoryHover=Hover(model,source,factory);Assert.Contains("Enumerator",factoryHover);Assert.Contains("int",factoryHover);
        Definition(model,source,factory,library,library.Text.IndexOf("GetEnumerator()",StringComparison.Ordinal));
        int move=text.IndexOf("MoveNext",StringComparison.Ordinal);
        string moveHover=Hover(model,source,move);Assert.Contains("bool",moveHover);Assert.Contains("nothrow",moveHover);Assert.DoesNotContain('$',moveHover);
        Definition(model,source,move,library,library.Text.IndexOf("MoveNext()",StringComparison.Ordinal));
        int current=text.IndexOf("Current;",StringComparison.Ordinal);
        string currentHover=Hover(model,source,current);Assert.Contains("int",currentHover);Assert.Contains("Current",currentHover);Assert.DoesNotContain('$',currentHover);
        int currentDeclaration=library.Text.IndexOf("T Current",StringComparison.Ordinal);Assert.True(currentDeclaration>=0);
        Definition(model,source,current,library,currentDeclaration+2);
        int result=text.LastIndexOf("current",StringComparison.Ordinal);Assert.Contains("int current",Hover(model,source,result));
    }

    private static (EditorModel Model,SourceFile Source,SourceFile Library) Analyze(string text)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        var files=new[]{"Collections/IEnumerable.gf","Collections/IEnumerator.gf","Queries/Enumerable.gf","Queries/Repeated.gf"}
            .Select(name=>Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="Repeated.gf");
        var source=new SourceFile(Path.GetFullPath("repeated-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,library);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void Definition(EditorModel model,SourceFile source,int use,SourceFile library,int declaration)
    {
        Assert.True(declaration>=0);var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));Assert.Contains("Repeated.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
    }
    private static string Color(EditorModel model,SourceFile source,int offset)
    {
        int[] data=Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();int column=0;
        for(int i=0;i<data.Length;i+=5){column=data[i]==0?column+data[i+1]:data[i+1];if(column==offset)return EditorModel.TokenTypes[data[i+3]];}
        Assert.Fail("Missing nested enumerator semantic token");return "";
    }
}
