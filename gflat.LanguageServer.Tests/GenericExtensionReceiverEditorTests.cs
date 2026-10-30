using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericExtensionReceiverEditorTests
{
    [Theory]
    [InlineData(false,"T","T")]
    [InlineData(true,"T","T")]
    [InlineData(false,"U","U")]
    [InlineData(true,"U","U")]
    [InlineData(false,"U","int")]
    [InlineData(false,"U","U*")]
    public void ExtensionCallsOnGenericNominalReceiversKeepTheirSourceDeclaration(bool checkedPath,string clientParameter,string receiverArgument)
        =>AssertExtensionCall(checkedPath,clientParameter,receiverArgument);

    [Fact]
    public void ConcreteTypeNamedTDoesNotBecomeAnOpenParameter()
        =>AssertExtensionCall(false,"U","T","struct T{}",resolved:false);

    [Fact]
    public void UnsupportedNestedSymbolicArgumentDoesNotInventAnExtensionBinding()
        =>AssertExtensionCall(false,"U","Box<U>",resolved:false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespacedGenericExtensionReceiverKeepsItsDeclaration(bool checkedPath)
        =>AssertExtensionCall(checkedPath,"U","U",libraryNamespace:"Library");

    private static void AssertExtensionCall(bool checkedPath,string clientParameter,string receiverArgument,string declarations="",bool resolved=true,string libraryNamespace="")
    {
        string text=declarations+"struct Box<T>{public T Value;}extension<T> Box<T>{public void Second(){}}extension<"+clientParameter+"> Box<"+receiverArgument+">{public void First(){(*this).Second();}}void Use(Box<int>* target){(*target).Second();}void Force(Box<"+receiverArgument.Replace(clientParameter,"int",StringComparison.Ordinal)+">* target){}";
        if(libraryNamespace.Length>0)text="namespace "+libraryNamespace+"{"+text+"}";
        var source=new SourceFile(Path.GetFullPath("generic-extension-receiver.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>()
            .Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        var spans=types.Select(type=>type.Span).ToArray();
        var changes=new List<string>();
        var model=new EditorModel(snapshot,[source]);
        CheckSpans("construction");
        int use=checkedPath?text.LastIndexOf("Second();",StringComparison.Ordinal):text.IndexOf("Second();",StringComparison.Ordinal);
        var hover=Json(model.Hover(source.Path,use));
        CheckSpans("hover");
        var completion=Json(model.Completion(source.Path,use));
        CheckSpans("completion");
        var signature=Json(model.SignatureHelp(source.Path,use+"Second(".Length));
        CheckSpans("signature");
        var definition=model.Definition(source.Path,use);
        CheckSpans("definition");
        int[] tokens=Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(item=>item.GetInt32()).ToArray();
        CheckSpans("semantic tokens");
        var classifications=new Dictionary<int,string>();int column=0;
        for(int index=0;index<tokens.Length;index+=5)
        {
            Assert.Equal(0,tokens[index]);
            column+=tokens[index+1];
            classifications[column]=EditorModel.TokenTypes[tokens[index+3]];
        }
        if(!resolved)
        {
            Assert.Equal(JsonValueKind.Null,hover.ValueKind);
            Assert.Null(definition);
            Assert.DoesNotContain(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Second");
            Assert.NotEqual("method",classifications.GetValueOrDefault(use));
            Assert.Empty(changes);
            return;
        }
        Assert.Empty(changes);
        Assert.Equal("method",classifications.GetValueOrDefault(use));
        string expected="void "+(libraryNamespace.Length==0?"":libraryNamespace+"::")+"Second()";
        Assert.Contains(expected,hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.DoesNotContain("$",hover.ToString());
        var item=Assert.Single(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Second");
        Assert.Contains(expected,item.GetProperty("detail").GetString());
        var selected=Assert.Single(signature.GetProperty("signatures").EnumerateArray());
        Assert.Contains(expected,selected.GetProperty("label").GetString());
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(text.IndexOf("Second()",StringComparison.Ordinal),"Second".Length))),
            Assert.IsType<Location>(definition));
        Assert.Empty(changes);

        void CheckSpans(string stage)
        {
            for(int index=0;index<types.Length;index++)
                if(!spans[index].Equals(types[index].Span))
                    changes.Add($"{stage}: {types[index].GetType().Name} [{spans[index].Start},{spans[index].Length}] changed to [{types[index].Span.Start},{types[index].Span.Length}]");
        }
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void OpenNominalReceiversKeepReadonlyAndStorageEligibility(bool temporary,bool namespaced)
    {
        string text="struct Box<T>{public int Marker;}extension<T> Box<T>{public void Mutable(){}}extension<T> Box<T>{public readonly int Safe()=>(*this).Marker;}extension<U> Box<U>{public readonly void Inspect(){int value=(*this).Marker;}}void Temporary<U>(){int value=new Box<U>().Marker;}";
        if(namespaced)text="namespace Library{"+text+"}";
        var source=new SourceFile(Path.GetFullPath("generic-extension-eligibility.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>()
            .Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        var spans=types.Select(type=>type.Span).ToArray();
        var model=new EditorModel(snapshot,[source]);
        int use=temporary?text.LastIndexOf(".Marker",StringComparison.Ordinal)+1:text.IndexOf("int value=(*this).Marker",StringComparison.Ordinal)+"int value=(*this).".Length;
        var names=Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray()
            .Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Marker",names);
        if(temporary)Assert.Contains("Mutable",names);else Assert.DoesNotContain("Mutable",names);
        Assert.Contains("Safe",names);
        Assert.Equal(spans,types.Select(type=>type.Span));
    }

    [Fact]
    public void NamespacedNominalReceiverPreservesDeclaredParameterCapabilities()
    {
        const string text="namespace Library{interface IProvider{static int Read();}interface IExtra{static int Extra();}struct Handler<P>{}extension<P:IProvider> Handler<P>{public int Visible()=>P::Read();}extension<P:IExtra> Handler<P>{public int Hidden()=>P::Extra();}extension<U:IProvider> Handler<U>{public int Inspect()=>(*this).Visible();}}";
        var source=new SourceFile(Path.GetFullPath("namespaced-extension-capabilities.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>().Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        var spans=types.Select(type=>type.Span).ToArray();
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf("Visible()",StringComparison.Ordinal);
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int Library::Visible()",hover);Assert.DoesNotContain("$",hover);
        var names=Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Visible",names);Assert.DoesNotContain("Hidden",names);
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(text.IndexOf("Visible()",StringComparison.Ordinal),"Visible".Length))),Assert.IsType<Location>(model.Definition(source.Path,use)));
        Assert.Equal(spans,types.Select(type=>type.Span));
    }

    [Fact]
    public void RootTypeParameterDoesNotBecomeANominalReceiverWithTheSameName()
    {
        const string library="namespace Library{interface IProtocol{int Marker{get;}}struct Box<T>{public int Marker;}struct Other<T>{public int Marker;}extension<T> Box<T>{public void Second(){}}}";
        string text=library+"namespace Library{extension<Box:IProtocol> Box{public int Inspect()=>(*this).Marker;}}";
        var source=new SourceFile(Path.GetFullPath("namespaced-extension-shadow.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>().Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        var spans=types.Select(type=>type.Span).ToArray();
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf(".Marker",StringComparison.Ordinal)+1;
        var names=Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.DoesNotContain("Second",names);
        Assert.Equal(spans,types.Select(type=>type.Span));
    }

    [Fact]
    public void NamespacedCheckedAliasReceiverPreservesItsExposedCapabilities()
    {
        const string text="namespace Library{interface IProvider{static int Read();}interface IExtra{static int Extra();}static class Answer:IProvider,IExtra{public static int Read()=>42;public static int Extra()=>7;}weak alias Selected:IProvider=Answer;struct Handler<P>{}extension<P:IProvider> Handler<P>{public int Visible()=>P::Read();}extension<P:IExtra> Handler<P>{public int Hidden()=>P::Extra();}int Cached(Handler<Answer> target)=>target.Hidden();int Inspect(Handler<Selected> target)=>target.Visible();}";
        var source=new SourceFile(Path.GetFullPath("namespaced-alias-capabilities.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,diagnostic=>diagnostic.Severity==DiagnosticSeverity.Error);
        var types=snapshot.Nodes.Select(node=>node.Node).OfType<TypeExpression>().Where(type=>type.Span.Source?.Id==source.Id).ToArray();
        var spans=types.Select(type=>type.Span).ToArray();
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf("Visible()",StringComparison.Ordinal);
        var names=Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Visible",names);Assert.DoesNotContain("Hidden",names);
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Library::Visible()",hover);Assert.DoesNotContain("$",hover);
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(text.IndexOf("Visible()",StringComparison.Ordinal),"Visible".Length))),Assert.IsType<Location>(model.Definition(source.Path,use)));
        Assert.Equal(spans,types.Select(type=>type.Span));
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
