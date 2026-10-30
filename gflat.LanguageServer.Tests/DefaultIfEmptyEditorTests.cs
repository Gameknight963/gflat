using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise the actual query sources, including their nested cursor declarations.
public sealed class DefaultIfEmptyEditorTests
{
    [Fact]
    public void ActualDefaultIfEmptyExtensionHasOneConcreteValueArgumentAndItsSourceDefinition()
    {
        const string text="using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;int Probe(){int[2] data=[1,2];Span<int> view=new(data,2);var query=view.AsEnumerable().DefaultIfEmpty(7);return 0;}";
        var (model,source,files)=Analyze(text);
        int call=text.IndexOf("DefaultIfEmpty",StringComparison.Ordinal);
        var items=Json(model.Completion(source.Path,call)).GetProperty("items");
        Assert.Single(items.EnumerateArray(),item=>item.GetProperty("label").GetString()=="DefaultIfEmpty");
        string hover=Hover(model,source,call);
        Assert.Contains("DefaultIfEmpty",hover);Assert.Contains("Enumerable",hover);Assert.Contains("int",hover);Assert.Contains("value",hover);
        Assert.DoesNotContain("T value",hover);Assert.DoesNotContain('$',hover);
        SourceFile extension=files.Single(file=>Path.GetFileName(file.Path)=="enumerable_default_if_empty.gf");
        int declaration=EditorSourceDeclarations.ExtensionMethod(extension,"DefaultIfEmpty");Assert.True(declaration>=0);
        Definition(model,source,call,extension,declaration);
        var help=Json(model.SignatureHelp(source.Path,text.IndexOf("(7)",call,StringComparison.Ordinal)+1));
        var signature=Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());
        var parameter=Assert.Single(signature.GetProperty("parameters").EnumerateArray());
        string value=parameter.GetProperty("label").GetString()!;
        Assert.Contains("int",value);Assert.Contains("value",value);Assert.DoesNotContain("T value",value);
        string label=signature.GetProperty("label").GetString()!;
        Assert.Contains("DefaultIfEmpty",label);Assert.Contains("Enumerable",label);Assert.DoesNotContain('$',label);
    }

    [Fact]
    public void NamedQueryCursorRetainsNestedStructIdentityAndConcreteCurrentServices()
    {
        const string text="using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;int Probe(){int[1] data=[7];Span<int> view=new(data,1);var query=view.AsEnumerable().DefaultIfEmpty(9);DefaultIfEmptySource<int,Span<int>,Span<int>::Enumerator>::Enumerator cursor=query.GetEnumerator();if(!cursor.MoveNext())return 1;int current=cursor.Current;return current;}";
        var (model,source,files)=Analyze(text);
        int type=text.IndexOf("Enumerator cursor",StringComparison.Ordinal);
        Assert.Equal("struct",Color(model,source,type));
        string hover=Hover(model,source,type);
        Assert.Contains("DefaultIfEmptySource<int",hover);Assert.Contains("Enumerator",hover);Assert.DoesNotContain('$',hover);
        SourceFile adapter=files.Single(file=>Path.GetFileName(file.Path)=="DefaultIfEmptySource.gf");
        int declaration=adapter.Text.IndexOf("struct Enumerator",StringComparison.Ordinal);Assert.True(declaration>=0);
        Definition(model,source,type,adapter,declaration+"struct ".Length);
        int current=text.IndexOf("Current;",StringComparison.Ordinal);
        string currentHover=Hover(model,source,current);Assert.Contains("int",currentHover);Assert.Contains("Current",currentHover);Assert.DoesNotContain('$',currentHover);
        int property=adapter.Text.IndexOf("T Current",StringComparison.Ordinal);Assert.True(property>=0);
        Definition(model,source,current,adapter,property+2);
        Assert.Contains("int current",Hover(model,source,text.LastIndexOf("current",StringComparison.Ordinal)));
    }

    private static (EditorModel Model,SourceFile Source,SourceFile[] Files) Analyze(string text)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["Collections/IEnumerable.gf","Collections/IEnumerator.gf","Queries/Enumerable.gf","Memory/Span.gf",
            "Queries/DefaultIfEmptySource.gf","Queries/enumerable_default_if_empty.gf"];
        var files=names.Select(name=>Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("default-if-empty-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,files.ToArray());
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile file,int offset)
        =>Json(model.Hover(file.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void Definition(EditorModel model,SourceFile file,int use,SourceFile library,int declaration)
    {
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(file.Path,use));
        Assert.Contains(Path.GetFileName(library.Path),JsonSerializer.Serialize(expected,Protocol.JsonOptions));
    }
    private static string Color(EditorModel model,SourceFile file,int offset)
    {
        int[] data=Json(model.SemanticTokens(file.Path)).GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();int column=0;
        for(int i=0;i<data.Length;i+=5){column=data[i]==0?column+data[i+1]:data[i+1];if(column==offset)return EditorModel.TokenTypes[data[i+3]];}
        Assert.Fail("Missing nested cursor semantic token");return "";
    }
}
