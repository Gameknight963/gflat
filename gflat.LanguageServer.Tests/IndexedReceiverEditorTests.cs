using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class IndexedReceiverEditorTests
{
    [Theory]
    [InlineData("pointer")]
    [InlineData("array")]
    [InlineData("readonly")]
    [InlineData("immutable")]
    [InlineData("cast")]
    [InlineData("parentheses")]
    public void OpenGenericBuiltinIndexedReceiversKeepTheirElementDeclarationsAndProtection(string form)
    {
        string storage=form switch {
            "array"=>"Entry[2]", "readonly"=>"readonly(Entry)*?", "immutable"=>"immutable(Entry)*?", _=>"Entry*?"
        };
        string receiver=form switch {
            "array"=>"data[0]", "cast"=>"((Entry*)data)[0]", "parentheses"=>"(data![0])", _=>"data![0]"
        };
        string text=$$"""
            struct Holder<T> {
                public struct Entry {
                    public int Value;
                    public void Mutate(){Value++;}
                    public readonly int Shallow()=>Value;
                    public immutable int Frozen()=>Value;
                }
                private {{storage}} data;
                public int Field()=>{{receiver}}.Value;
                public int Method()=>{{receiver}}.Frozen();
            }
            int Trigger(Holder<int>* value)=>0;
            """;
        var (model,source)=Analyze(text);
        int field=text.IndexOf(receiver+".Value",StringComparison.Ordinal)+receiver.Length+1;
        int method=text.IndexOf(receiver+".Frozen",StringComparison.Ordinal)+receiver.Length+1;
        AssertBinding(model,source,field,text.IndexOf("int Value",StringComparison.Ordinal)+4,"Value","property");
        AssertBinding(model,source,method,text.IndexOf("int Frozen",StringComparison.Ordinal)+4,"Frozen","method");
        var names=CompletionNames(model,source,field);
        Assert.Contains("Value",names);Assert.Contains("Frozen",names);
        if(form is "readonly" or "immutable")Assert.DoesNotContain("Mutate",names);else Assert.Contains("Mutate",names);
        if(form=="immutable")Assert.DoesNotContain("Shallow",names);else Assert.Contains("Shallow",names);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenGenericReferenceAndAliasPointerParametersKeepElementDeclarations(bool alias)
    {
        string parameter=alias?"Storage":"ref Entry*";
        string text=$$"""
            struct Entry {public int Value;}
            alias Storage=Entry*;
            struct Holder<T> {public int Read({{parameter}} data)=>data[0].Value;}
            int Trigger(Holder<int>* value)=>0;
            """;
        var (model,source)=Analyze(text);
        int use=text.IndexOf("].Value",StringComparison.Ordinal)+2;
        AssertBinding(model,source,use,text.IndexOf("int Value",StringComparison.Ordinal)+4,"Value","property");
        Assert.Contains("Value",CompletionNames(model,source,use));
    }

    [Theory]
    [InlineData("Storage",true)]
    [InlineData("readonly(Storage)",true)]
    [InlineData("immutable(Storage)",true)]
    [InlineData("Storage",false)]
    [InlineData("readonly(Storage)",false)]
    [InlineData("immutable(Storage)",false)]
    public void PointerAliasesKeepDeclarationContextWhenCallerHasCompetingEntry(string parameter,bool qualified)
    {
        string definitions=$$"""
            namespace Definitions;
            public struct Entry {
                public int Value;
                public void Mutate(){Value++;}
                public immutable int Frozen()=>Value;
            }
            public alias Storage={{(qualified?"Definitions::Entry":"Entry")}}*;
            """;
        var library=new SourceFile(Path.GetFullPath("indexed-definitions.gf"),definitions);
        string text=$$"""
            using Definitions;
            struct Holder<T> {
                public struct Entry {public int WrongMember;}
                public int Field({{parameter}} data)=>data[0].Value;
                public int Method({{parameter}} data)=>data[0].Frozen();
            }
            int Trigger(Holder<int>* value)=>0;
            """;
        var (model,source)=Analyze(text,library);
        int field=text.IndexOf("].Value",StringComparison.Ordinal)+2;
        int method=text.IndexOf("].Frozen",StringComparison.Ordinal)+2;
        AssertBinding(model,source,field,definitions.IndexOf("int Value",StringComparison.Ordinal)+4,"Value","property",library);
        AssertBinding(model,source,method,definitions.IndexOf("int Frozen",StringComparison.Ordinal)+4,"Frozen","method",library);
        var names=CompletionNames(model,source,field);
        Assert.Contains("Value",names);Assert.Contains("Frozen",names);Assert.DoesNotContain("WrongMember",names);
        if(parameter.StartsWith("immutable",StringComparison.Ordinal))Assert.DoesNotContain("Mutate",names);
        else Assert.Contains("Mutate",names);
    }

    [Fact]
    public void CheckedCustomIndexerUsesActualReturnRatherThanFirstGenericArgument()
    {
        const string text="""
            struct Wrong {public int WrongMember;}
            struct Actual {public int ActualMember;}
            struct Indexed<T> {public Actual this[int index] {get=>new Actual();}}
            int Read(Indexed<Wrong>* value)=>(*value)[0].ActualMember;
            """;
        var (model,source)=Analyze(text);
        int use=text.IndexOf("].ActualMember",StringComparison.Ordinal)+2;
        AssertBinding(model,source,use,text.IndexOf("int ActualMember",StringComparison.Ordinal)+4,"ActualMember","property");
        var names=CompletionNames(model,source,use);
        Assert.Contains("ActualMember",names);Assert.DoesNotContain("WrongMember",names);
    }

    private static (EditorModel Model,SourceFile Source) Analyze(string text,params SourceFile[] libraries)
    {
        var source=new SourceFile(Path.GetFullPath("indexed-receiver.gf"),text);
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
