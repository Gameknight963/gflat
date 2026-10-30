using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EditorSnapshotIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericCallEditorQueriesPreserveEveryAnalyzedSourceTypeSpan(bool extension)
    {
        string text=extension
            ? "struct Box<T>{public T Value;}extension<T> Box<T>{public T Read()=>(*this).Value;}int Use(Box<int>* box)=>(*box).Read();"
            : "T Echo<T>(T value)=>value;int Read()=>Echo<int>(1);";
        string name=extension?"Read":"Echo";
        string call=extension?"Read()":"Echo<int>(1)";
        var source=new SourceFile(Path.GetFullPath("editor-snapshot-integrity.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>()
            .Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        Assert.NotEmpty(types);
        var original=types.Select(type=>type.Span).ToArray();
        var changes=new List<string>();
        var model=new EditorModel(snapshot,[source]);
        CheckSpans("construction");
        int use=text.LastIndexOf(call,StringComparison.Ordinal);
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(name,hover);Assert.Contains("int",hover);
        Assert.DoesNotContain("$",hover);
        CheckSpans("hover");
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span((extension?EditorSourceDeclarations.ExtensionMethod(source,name):text.IndexOf(name+"<T>",StringComparison.Ordinal)),name.Length))),
            Assert.IsType<Location>(model.Definition(source.Path,use)));
        CheckSpans("definition");
        var completion=Json(model.Completion(source.Path,use));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==name);
        CheckSpans("completion");
        int signature=text.IndexOf('(',use)+1;
        var help=Json(model.SignatureHelp(source.Path,signature));
        Assert.Contains(help.GetProperty("signatures").EnumerateArray(),item=>item.GetProperty("label").GetString()!.Contains(name,StringComparison.Ordinal));
        CheckSpans("signature");
        Assert.NotEmpty(Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray());
        CheckSpans("semantic tokens");
        Assert.Empty(changes);

        void CheckSpans(string stage)
        {
            for(int index=0;index<types.Length;index++)
                if(!original[index].Equals(types[index].Span))
                    changes.Add($"{stage}: {types[index].GetType().Name} source [{original[index].Start},{original[index].Length}] changed to [{types[index].Span.Start},{types[index].Span.Length}]");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericInstanceMemberQueriesPreserveEveryAnalyzedSourceTypeSpan(bool returnsBox)
    {
        string name=returnsBox?"Slice":"Put";
        string member=returnsBox?"public Box<T> Slice()=>new();":"public void Put(T value){}";
        string call=returnsBox?"Slice()":"Put(1)";
        string text="struct Box<T>{"+member+"}void Use(Box<int>* value){value."+call+";}";
        var source=new SourceFile(Path.GetFullPath("instance-snapshot-integrity.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>()
            .Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        var original=types.Select(type=>type.Span).ToArray();
        var changes=new List<string>();
        var model=new EditorModel(snapshot,[source]);
        CheckSpans("construction");
        int use=text.LastIndexOf(call,StringComparison.Ordinal);
        string expected=returnsBox?"Box<int> Box<int>.Slice()":"void Box<int>.Put(int value)";
        string expectedLabel=expected.Replace("."+name,"::"+name,StringComparison.Ordinal);
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(expected,hover);Assert.DoesNotContain("$",hover);
        CheckSpans("hover");
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(text.IndexOf(name+"(",StringComparison.Ordinal),name.Length))),
            Assert.IsType<Location>(model.Definition(source.Path,use)));
        CheckSpans("definition");
        var completion=Json(model.Completion(source.Path,use));
        var item=Assert.Single(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==name);
        Assert.Contains(expectedLabel,item.GetProperty("detail").GetString());
        CheckSpans("completion");
        var help=Json(model.SignatureHelp(source.Path,text.IndexOf('(',use)+1));
        Assert.Contains(expectedLabel,Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString());
        CheckSpans("signature");
        Assert.NotEmpty(Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray());
        CheckSpans("semantic tokens");
        Assert.Empty(changes);

        void CheckSpans(string stage)
        {
            for(int index=0;index<types.Length;index++)
                if(!original[index].Equals(types[index].Span))
                    changes.Add($"{stage}: {types[index].GetType().Name} source [{original[index].Start},{original[index].Length}] changed to [{types[index].Span.Start},{types[index].Span.Length}]");
        }
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
