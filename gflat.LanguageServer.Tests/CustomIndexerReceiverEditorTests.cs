using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CustomIndexerReceiverEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomIndexerKeepsItsIndependentElementDeclarationAndImmutableView(bool checkedPath)
    {
        const string definitions="""
            namespace Library;
            public struct Entry {
                public int Value;
                public void Mutate(){Value++;}
                public immutable int Read()=>Value;
            }
            public struct Buffer<T> {
                private T*? data;
                public ref T this[nuint index] {immutable get=>data![index];}
            }
            """;
        var library=new SourceFile(Path.GetFullPath("custom-indexer-library.gf"),definitions);
        string parameters=checkedPath?"":"<U>";
        string arguments=checkedPath?"":"<int>";
        string text=$$"""
            using Library;
            struct Caller{{parameters}} {
                private Buffer<immutable(Entry)> data;
                public int Inspect(nuint index)=>data[index].Value;
            }
            int Trigger(Caller{{arguments}}* value)=>0;
            """;
        var (model,source)=Analyze(text,library);
        int use=text.IndexOf("].Value",StringComparison.Ordinal)+2;
        AssertBinding(model,source,use,definitions.IndexOf("int Value",StringComparison.Ordinal)+4,"Value","property",library);
        string hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int",hover);
        var names=CompletionNames(model,source,use);
        Assert.Contains("Value",names);Assert.Contains("Read",names);Assert.DoesNotContain("Mutate",names);
    }

    [Fact]
    public void RawPointerIndexingKeepsTheCustomIndexerOwnerAsItsElement()
    {
        const string text="struct Entry{public int Value;}struct Buffer<T>{public int Marker;public Entry this[int index]=>new();}struct Caller<U>{public int Read(Buffer<int>* data)=>data[0].Marker;}int Trigger(Caller<int>* value)=>0;";
        var (model,source)=Analyze(text);
        int use=text.IndexOf("].Marker",StringComparison.Ordinal)+2;
        AssertBinding(model,source,use,text.IndexOf("int Marker",StringComparison.Ordinal)+4,"Marker","property");
        var names=CompletionNames(model,source,use);
        Assert.Contains("Marker",names);Assert.DoesNotContain("Value",names);
    }

    [Theory]
    [InlineData("readonly","immutable",false)]
    [InlineData("immutable","immutable",false)]
    [InlineData("readonly","readonly",true)]
    public void ReferenceIndexerCompletionRespectsReceiverView(string qualifier,string getterQualifier,bool mutableResult)
    {
        string text=$$$"""
            struct Entry{public int Value;public void Mutate(){Value++;}public immutable int Read()=>Value;}
            struct Buffer<T>{private T*? data;public ref T this[int index]{ {{{getterQualifier}}} get=>data![index];}}
            struct Caller<U>{public int Inspect({{{qualifier}}}(Buffer<Entry>)* data)=>(*data)[0].Value;}
            int Trigger(Caller<int>* value)=>0;
            """;
        var (model,source)=Analyze(text);
        int use=text.IndexOf("].Value",StringComparison.Ordinal)+2;
        AssertBinding(model,source,use,text.IndexOf("int Value",StringComparison.Ordinal)+4,"Value","property");
        var names=CompletionNames(model,source,use);
        Assert.Contains("Value",names);Assert.Contains("Read",names);
        Assert.Equal(mutableResult,names.Contains("Mutate"));
    }

    [Theory]
    [InlineData("private T this[int index]{get=>new();}","Cannot access private getter of property 'this[]'")]
    [InlineData("public T this[int first,int second]{get=>new();}","Indexer accessor 'this[]' expects 2 arguments but got 1")]
    [InlineData("public T this[int index]{set{}}","Property 'this[]' has no getter")]
    public void GenericBodyRecoveryDoesNotInventAnUnavailableIndexerResult(string indexer,string expectedError)
    {
        string text="struct Entry{public int Value;}struct Buffer<T>{"+indexer+"}struct Caller<U>{public int Inspect(Buffer<Entry> data)=>data[0].Value;}int Trigger(Caller<int>* value)=>0;";
        var source=new SourceFile(Path.GetFullPath("custom-indexer-recovery.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        var errors=snapshot.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors,error=>Assert.Equal(expectedError,error.Message));
        var model=new EditorModel(snapshot,[source]);
        int use=text.IndexOf("].Value",StringComparison.Ordinal)+2;
        Assert.Null(model.Hover(source.Path,use));
        Assert.Null(model.Definition(source.Path,use));
        Assert.NotEqual("property",Classifications(model,source).GetValueOrDefault(use));
        Assert.DoesNotContain("Value",CompletionNames(model,source,use));
    }

    [Fact]
    public void IndexerProjectionPreservesSourceSpansAndDoesNotBindUnrelatedGlobalTypeThroughAnAlias()
    {
        foreach (var fixture in new[]{(Receiver:"Buffer<Entry>",Checked:false),(Receiver:"View",Checked:true),(Receiver:"View",Checked:false)})
        {
            string parameter=fixture.Checked?"":"<U>";
            string callerArgument=fixture.Checked?"":"<int>";
            string text="struct T{public int Wrong;}struct Entry{public int Value;}struct Buffer<T>{private T*? data;public ref T this[int index]{get=>data![index];}}alias View=Buffer<Entry>;struct Caller"+parameter+"{public int Inspect("+fixture.Receiver+" data)=>data[0].Value;}int Trigger(Caller"+callerArgument+"* value)=>0;";
            var source=new SourceFile(Path.GetFullPath("custom-indexer-alias.gf"),text);
            var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
            Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
            var arguments=snapshot.Nodes.Select(node=>node.Node).OfType<gflat.ast.NamedTypeExpression>()
                .Where(type=>type.Name=="Buffer").Select(type=>type.TypeArguments[0]).ToArray();
            var original=arguments.Select(argument=>argument.Span).ToArray();
            var model=new EditorModel(snapshot,[source]);
            int use=text.IndexOf("].Value",StringComparison.Ordinal)+2;
            var names=CompletionNames(model,source,use);
            Assert.DoesNotContain("Wrong",names);
            if(fixture.Receiver=="View"&&!fixture.Checked)
            {
                Assert.Null(model.Hover(source.Path,use));
                Assert.Null(model.Definition(source.Path,use));
                Assert.NotEqual("property",Classifications(model,source).GetValueOrDefault(use));
            }
            else
            {
                AssertBinding(model,source,use,text.IndexOf("int Value",StringComparison.Ordinal)+4,"Value","property");
                Assert.Contains("Value",names);
            }
            Assert.Equal(original,arguments.Select(argument=>argument.Span));
        }
    }

    private static (EditorModel Model,SourceFile Source) Analyze(string text,params SourceFile[] libraries)
    {
        var source=new SourceFile(Path.GetFullPath("custom-indexer-primary.gf"),text);
        var sources=libraries.Append(source).ToArray();
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        return (new EditorModel(snapshot,sources),source);
    }
    private static string[] CompletionNames(EditorModel model,SourceFile source,int offset)
        =>JsonSerializer.SerializeToElement(model.Completion(source.Path,offset),Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()!).ToArray();
    private static void AssertBinding(EditorModel model,SourceFile source,int use,int declaration,string name,string kind,SourceFile? declarationSource=null)
    {
        Assert.True(use>=0&&declaration>=0);
        Assert.Equal(kind,Classifications(model,source).GetValueOrDefault(use));
        var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions);
        Assert.NotEqual(JsonValueKind.Null,hover.ValueKind);
        string description=hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(name,description);Assert.DoesNotContain("$",description);
        Assert.Equal(new Location(Workspace.FileUri((declarationSource??source).Path),Workspace.ToRange((declarationSource??source).Span(declaration,name.Length))),
            Assert.IsType<Location>(model.Definition(source.Path,use)));
    }
    private static Dictionary<int,string> Classifications(EditorModel model,SourceFile source)
    {
        int[] data=JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path),Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();
        var starts=new List<int>{0};for(int i=0;i<source.Text.Length;i++)if(source.Text[i]=='\n')starts.Add(i+1);
        var result=new Dictionary<int,string>();int line=0,column=0;
        for(int i=0;i<data.Length;i+=5){line+=data[i];column=data[i]==0?column+data[i+1]:data[i+1];result[starts[line]+column]=EditorModel.TokenTypes[data[i+3]];}
        return result;
    }
}
