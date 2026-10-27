using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NestedTypeVisibilityEditorTests
{
    [Theory]
    [InlineData("class",false)]
    [InlineData("struct",false)]
    [InlineData("struct",true)]
    public void NestedPeerTypesAndExplicitPrivateReceiversUseTheirRealDeclarations(string kind,bool generic)
    {
        string parameters=generic?"<T>":"";
        string text=$$"""
            namespace Models {
                public {{kind}} Owner{{parameters}} {
                    private int secret;
                    public {{kind}} Node { public Node* Next; }
                    private {{kind}} State { public Node* First; }
                    public {{kind}} Inner {
                        private int own;
                        private State* state;
                        public static int ReadOuter(Owner{{parameters}}* outer)=>outer.secret;
                    }
                    public static int ReadInner(Inner* inner)=>inner.own;
                }
            }
            """;
        if(generic)text+="\nint Trigger(Models::Owner<int>* value)=>0;";
        var (model,source)=Analyze(text);
        string TypeName(string name)=>kind+" "+name;
        int NodeDeclaration()=>text.IndexOf(TypeName("Node"),StringComparison.Ordinal)+kind.Length+1;
        int stateDeclaration=text.IndexOf(TypeName("State"),StringComparison.Ordinal)+kind.Length+1;
        AssertBinding(model,source,text.IndexOf("Node* Next",StringComparison.Ordinal),NodeDeclaration(),"Node",kind);
        AssertBinding(model,source,text.IndexOf("Node* First",StringComparison.Ordinal),NodeDeclaration(),"Node",kind);
        AssertBinding(model,source,text.IndexOf("State* state",StringComparison.Ordinal),stateDeclaration,"State",kind);
        AssertBinding(model,source,text.IndexOf("outer.secret",StringComparison.Ordinal)+6,
            text.IndexOf("int secret",StringComparison.Ordinal)+4,"secret","property");
        AssertBinding(model,source,text.IndexOf("inner.own",StringComparison.Ordinal)+6,
            text.IndexOf("int own",StringComparison.Ordinal)+4,"own","property");
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void EnclosingNestedTypesKeepCompilerPriorityOverNamespaceAliases(bool receiver,bool globalType)
    {
        const string kind="class";
        // Adapted from the existing passing compiler LexicalNestedTypeLookupTests.
        string text=$$"""
            {{(globalType?"struct Word { public byte wrong; }":"")}}
            namespace Library {
                {{(globalType?"":"alias Word=int;")}}
                {{kind}} Owner {
                    public struct Word { public long value; }
                    public struct Box<T> {
                        public static int Read()=>sizeof(Word);
                        public static long ReadField(Word* value)=>value.value;
                    }
                }
            }
            int main()=>Library::Owner::Box<int>::Read();
            """;
        var (model,source)=Analyze(text);
        if(receiver)AssertBinding(model,source,text.IndexOf("value.value",StringComparison.Ordinal)+6,
            text.IndexOf("long value",StringComparison.Ordinal)+5,"value","property");
        else AssertBinding(model,source,text.IndexOf("sizeof(Word)",StringComparison.Ordinal)+7,
            text.IndexOf("struct Word { public long",StringComparison.Ordinal)+7,"Word","struct");
    }

    [Fact]
    public void QualifiedGenericMethodUsesItsBoundOwnerForContainingPrivateAccess()
    {
        const string text="""
            class Owner { private int Hidden; public class Inner { public expect static int Read<T>(Owner* value); } }
            alias OwnerAlias=Owner;
            public replace static int Owner::Inner::Read<T>(OwnerAlias* value)=>value.Hidden;
            int Trigger(OwnerAlias* value)=>Owner::Inner::Read<int>(value);
            """;
        var (model,source)=Analyze(text);
        AssertBinding(model,source,text.IndexOf("value.Hidden",StringComparison.Ordinal)+6,
            text.IndexOf("int Hidden",StringComparison.Ordinal)+4,"Hidden","property");
    }

    [Theory]
    [InlineData("sibling")]
    [InlineData("unrelated")]
    [InlineData("same-prefix")]
    [InlineData("namespace-prefix")]
    public void PrivateAccessDoesNotCrossSiblingUnrelatedOrNamespaceBoundaries(string form)
    {
        string accessor=form switch {
            "sibling"=>"", "unrelated"=>"class Other { public int Read(Owner* value)=>value.Visible; }",
            "same-prefix"=>"class OwnerExtra { public int Read(Owner* value)=>value.Visible; }",
            _=>"namespace Owner { class Nested { public int Read(OwnerAlias* value)=>value.Visible; } }"
        };
        string sibling=form=="sibling"?"public class Second { public int Read(First* value)=>value.Visible; }":"";
        string text=$$"""
            class Owner {
                private int Hidden; protected int ProtectedValue; public int Visible;
                public class First { private int Hidden; protected int ProtectedValue; public int Visible; }
                {{sibling}}
            }
            alias OwnerAlias=Owner;
            {{accessor}}
            """;
        var (model,source)=Analyze(text);
        int offset=text.LastIndexOf("value.Visible",StringComparison.Ordinal)+6;
        var labels=CompletionNames(model,source,offset);
        Assert.Contains("Visible",labels);
        Assert.DoesNotContain("Hidden",labels);Assert.DoesNotContain("ProtectedValue",labels);
    }

    [Fact]
    public void NearerTypesAndLocalAliasesWinWithoutImportingOuterInstanceFields()
    {
        const string text="""
            class Owner {
                private int OuterSecret;
                public struct Word { public byte OuterMarker; }
                public class Middle {
                    public struct Word { public long MiddleMarker; }
                    public class Inner<T> {
                        public Word* data;
                        public int Own;
                        public int Read()=>0;
                        public int Local(){alias Word=int;Word value=7;return value;}
                    }
                }
            }
            int Trigger(Owner::Middle::Inner<int>* value)=>0;
            """;
        var (model,source)=Analyze(text);
        AssertBinding(model,source,text.IndexOf("Word* data",StringComparison.Ordinal),
            text.LastIndexOf("struct Word",StringComparison.Ordinal)+7,"Word","struct");
        AssertBinding(model,source,text.IndexOf("Word value",StringComparison.Ordinal),
            text.IndexOf("alias Word",StringComparison.Ordinal)+6,"Word","type");
        var labels=CompletionNames(model,source,text.IndexOf("=>0",StringComparison.Ordinal)+2);
        Assert.Contains("Word",labels);Assert.Contains("Own",labels);Assert.DoesNotContain("OuterSecret",labels);
    }

    [Fact]
    public void EnclosingTypeBeatsInheritedNestedTypeInATypePosition()
    {
        // Independent compiler control emits a constant8 for this sizeof.
        const string text="""
            class Base { public struct Word { public byte value; } }
            class Owner {
                public struct Word { public long value; }
                public class Inner<T> : Base { public static int Read()=>sizeof(Word); }
            }
            int main()=>Owner::Inner<int>::Read();
            """;
        var (model,source)=Analyze(text);
        AssertBinding(model,source,text.IndexOf("sizeof(Word)",StringComparison.Ordinal)+7,
            text.LastIndexOf("struct Word",StringComparison.Ordinal)+7,"Word","struct");
    }

    [Fact]
    public void InheritedValueAndEnclosingTypeRetainTheirSeparateRoles()
    {
        // Compiler control emits add i32 7,8: value lookup and type lookup differ.
        const string text="""
            class Base { public static const int Word=7; }
            class Owner {
                public struct Word { public long value; }
                public class Inner<T> : Base { public static int Read()=>Word+sizeof(Word); }
            }
            int main()=>Owner::Inner<int>::Read();
            """;
        var (model,source)=Analyze(text);
        AssertBinding(model,source,text.IndexOf("=>Word+",StringComparison.Ordinal)+2,
            text.IndexOf("int Word",StringComparison.Ordinal)+4,"Word","property");
        AssertBinding(model,source,text.IndexOf("sizeof(Word)",StringComparison.Ordinal)+7,
            text.IndexOf("struct Word",StringComparison.Ordinal)+7,"Word","struct");
    }

    private static (EditorModel Model,SourceFile Source) Analyze(string text)
    {
        var source=new SourceFile(Path.GetFullPath("nested-type-visibility.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        return (new EditorModel(snapshot,[source]),source);
    }
    private static string[] CompletionNames(EditorModel model,SourceFile source,int offset)
        =>JsonSerializer.SerializeToElement(model.Completion(source.Path,offset),Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()!).ToArray();
    private static void AssertBinding(EditorModel model,SourceFile source,int use,int declaration,string name,string kind)
    {
        Assert.True(use>=0&&declaration>=0);
        Assert.Equal(kind,Classifications(model,source).GetValueOrDefault(use));
        var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions);
        Assert.NotEqual(JsonValueKind.Null,hover.ValueKind);
        string description=hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(name,description);Assert.DoesNotContain("$",description);
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(declaration,name.Length))),
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
