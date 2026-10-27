using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class UnconstrainedReceiverEditorTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void OpenParameterDoesNotAcquireMembersFromASameNamedConcreteType(bool lexical,bool staticAccess)
    {
        const string concrete="public struct T { public int FalseMember; public static int FalseStatic; }";
        string text=(lexical?"":concrete)+" struct Owner { "+(lexical?concrete:"")+"""
            public struct Inner<T> {
                public static int Read(T* value)=>value.FalseMember;
                public static int ReadStatic()=>T::FalseStatic;
            }}
            """;
        var (model,source)=Analyze(text);
        string name=staticAccess?"FalseStatic":"FalseMember";
        int use=text.LastIndexOf(name,StringComparison.Ordinal);
        Assert.Null(model.Hover(source.Path,use));
        Assert.Null(model.Definition(source.Path,use));
        Assert.False(Classifications(model,source).ContainsKey(use));
        var labels=JsonSerializer.SerializeToElement(model.Completion(source.Path,use),Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.DoesNotContain(name,labels);
    }

    [Theory]
    [InlineData("global",false)]
    [InlineData("global",true)]
    [InlineData("qualified",false)]
    [InlineData("qualified",true)]
    [InlineData("declaration-import",false)]
    [InlineData("declaration-import",true)]
    public void ConcreteAliasTargetRemainsKnownInsideAShadowingGenericScope(string form,bool staticAccess)
    {
        const string typeBody="public struct T { public int RealMember; public static int RealStatic=7; }";
        string types=form=="global"?typeBody:"namespace Definitions { "+typeBody+" }";
        if(form=="qualified")types+=" struct T { public int WrongMember; public static int WrongStatic; }";
        string alias=form switch {"global"=>"alias Concrete=T;","qualified"=>"namespace Definitions { public alias Concrete=T; }",_=>"using Definitions; alias Concrete=T;"};
        string parameter=form=="qualified"?"Definitions::Concrete":"Concrete";
        var declarationSource=new SourceFile(Path.GetFullPath("known-alias/a-types.gf"),types);
        var aliasSource=new SourceFile(Path.GetFullPath("known-alias/b-alias.gf"),alias);
        var source=new SourceFile(Path.GetFullPath("known-alias/c-client.gf"),$$"""
            struct Owner { public struct Inner<T> {
                public static int Read({{parameter}}* value)=>value.RealMember;
                public static int ReadStatic()=>{{parameter}}::RealStatic;
            } }
            """);
        SourceFile[] sources=[declarationSource,aliasSource,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        string name=staticAccess?"RealStatic":"RealMember";
        int use=source.Text.LastIndexOf(name,StringComparison.Ordinal);
        int declaration=types.IndexOf(name,StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path,use));
        Assert.Equal("property",Classifications(model,source).GetValueOrDefault(use));
        Assert.Equal(new Location(Workspace.FileUri(declarationSource.Path),Workspace.ToRange(declarationSource.Span(declaration,name.Length))),
            Assert.IsType<Location>(model.Definition(source.Path,use)));
    }

    private static (EditorModel Model,SourceFile Source) Analyze(string text)
    {
        var source=new SourceFile(Path.GetFullPath("unconstrained-receiver.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        return (new EditorModel(snapshot,[source]),source);
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
