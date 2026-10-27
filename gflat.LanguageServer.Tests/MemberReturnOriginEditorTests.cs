using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class MemberReturnOriginEditorTests
{
    [Theory]
    [InlineData("T","immutable(T)*",false)]
    [InlineData("long","readonly(long)*",false)]
    [InlineData("long","readonly(long)*",true)]
    public void OpenGenericPropertyChainsKeepTheDeclaringNestedTypeAndAppliedKey(string argument,string expectedType,bool checkedPath)
    {
        const string definitions="""
            namespace Library;
            public struct Producer<K> {
                public struct Entry {
                    private K value;
                    public Entry(K item){value=move item;}
                    public immutable(K)* Key {immutable get=> &value;}
                    public void Mutate(){}
                    private int Secret;
                }
                public struct Cursor {
                    private Entry*? data;
                    public Cursor(Entry* storage){data=storage;}
                    public immutable(Entry)* Current {immutable get=>data!;}
                }
            }
            """;
        var library=new SourceFile(Path.GetFullPath("member-return-library.gf"),definitions);
        string parameters=checkedPath?"":"<T>";
        string arguments=checkedPath?"":"<int>";
        string text=$$"""
            using Library;
            struct Wrapper{{parameters}} {
                public struct Entry {public int WrongMember;}
                private Producer<{{argument}}>::Cursor iterator;
                public immutable({{argument}})* Read()=>iterator.Current.Key;
            }
            int Trigger(Wrapper{{arguments}}* value)=>0;
            """;
        var (model,source)=Analyze(text,library);
        int use=text.IndexOf(".Key",StringComparison.Ordinal)+1;
        int declaration=definitions.IndexOf(" Key {",StringComparison.Ordinal)+1;
        AssertBinding(model,source,use,declaration,"Key","property",library);
        string hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(expectedType,hover);
        Assert.DoesNotContain("immutable(K)*",hover);
        var names=CompletionNames(model,source,use);
        Assert.Contains("Key",names);Assert.DoesNotContain("WrongMember",names);Assert.DoesNotContain("Mutate",names);Assert.DoesNotContain("Secret",names);
    }

    [Fact]
    public void AppliedOwnerArgumentsKeepBothDeclaredPositionsWhenSwapped()
    {
        const string definitions="""
            namespace Library;
            public struct PairProducer<A,B> {
                public struct Entry {
                    private A first; private B second;
                    public Entry(A a,B b){first=move a;second=move b;}
                    public immutable(A)* First {immutable get=> &first;}
                    public immutable(B)* Second {immutable get=> &second;}
                    private int Secret;
                }
                public struct Cursor {
                    private Entry*? data;
                    public Cursor(Entry* storage){data=storage;}
                    public immutable(Entry)* Current {immutable get=>data!;}
                }
            }
            """;
        var library=new SourceFile(Path.GetFullPath("member-return-pair-library.gf"),definitions);
        const string text="""
            using Library;
            struct Wrapper<X,Y> {
                public struct Entry {public int WrongMember;}
                private PairProducer<Y,X>::Cursor iterator;
                public immutable(Y)* ReadFirst()=>iterator.Current.First;
                public immutable(X)* ReadSecond()=>iterator.Current.Second;
            }
            int Trigger(Wrapper<int,long>* value)=>0;
            """;
        var (model,source)=Analyze(text,library);
        foreach(var (member,expected) in new[]{("First","immutable(Y)*"),("Second","immutable(X)*")})
        {
            int use=text.IndexOf("."+member,StringComparison.Ordinal)+1;
            int declaration=definitions.IndexOf(" "+member+" {",StringComparison.Ordinal)+1;
            AssertBinding(model,source,use,declaration,member,"property",library);
            string hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions)
                .GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(expected,hover);
            var names=CompletionNames(model,source,use);
            Assert.Contains("First",names);Assert.Contains("Second",names);Assert.DoesNotContain("WrongMember",names);Assert.DoesNotContain("Secret",names);
        }
    }

    [Fact]
    public void ProjectingAnOpenGenericFieldDoesNotChangeTheAnalyzedSourceArgument()
    {
        var library=new SourceFile(Path.GetFullPath("member-field-library.gf"),"""
            namespace Library;
            public struct Producer<K> {
                public struct Entry {public K Value;}
                public struct Cursor {
                    private Entry*? data;
                    public immutable(Entry)* Current {immutable get=>data!;}
                }
            }
            """);
        var source=new SourceFile(Path.GetFullPath("member-field-primary.gf"),"""
            using Library;
            struct Wrapper<T> {
                private Producer<T>::Cursor iterator;
                public T Read()=>iterator.Current.Value;
            }
            int Trigger(Wrapper<int>* value)=>0;
            """);
        var snapshot=new AnalysisSnapshot([library,source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var argument=snapshot.Nodes.Select(node=>node.Node).OfType<gflat.ast.NamedTypeExpression>()
            .Single(type=>type.Name=="Producer").TypeArguments[0];
        var original=argument.Span;
        var model=new EditorModel(snapshot,[library,source]);
        Assert.Equal(original,argument.Span);
        int use=source.Text.IndexOf(".Value",StringComparison.Ordinal)+1;
        AssertBinding(model,source,use,library.Text.IndexOf("K Value",StringComparison.Ordinal)+2,"Value","property",library);
        Assert.Equal(original,argument.Span);
    }

    private static (EditorModel Model,SourceFile Source) Analyze(string text,params SourceFile[] libraries)
    {
        var source=new SourceFile(Path.GetFullPath("member-return-primary.gf"),text);
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
