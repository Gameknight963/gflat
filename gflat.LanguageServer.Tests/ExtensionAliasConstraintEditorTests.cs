using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ExtensionAliasConstraintEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionHonorsProviderAliasExposure(bool cached)
    {
        string marked=$$"""
            interface IProvider {static int Read();}
            interface IExtra {static int Extra();}
            static class Answer:IProvider,IExtra {
                public static int Read()=>42;
                public static int Extra()=>7;
            }
            weak alias Selected:IProvider=Answer;
            struct Handler<P>{}
            int Visible<P:IProvider>(this Handler<P> value)=>P::Read();
            int Hidden<P:IExtra>(this Handler<P> value)=>P::Extra();
            int main(){
                {{(cached ? "Handler<Answer> first=new Handler<Answer>();first.Hidden();" : "")}}
                Handler<Selected> value=new Handler<Selected>();
                value.|;
                return 0;
            }
            """;
        int offset=marked.IndexOf('|');
        var source=new SourceFile(Path.GetFullPath("extension-alias-constraint.gf"),marked.Remove(offset,1));
        var snapshot=new AnalysisSnapshot([source]);
        var model=new EditorModel(snapshot,[source]);
        var unit=Assert.Single(snapshot.Nodes.Select(n=>n.Node).OfType<gflat.ast.CompilationUnit>());
        int members=unit.Members.Count;
        for(int i=0;i<5;i++) {
            var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,offset),Protocol.JsonOptions);
            var labels=completion.GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
            Assert.Contains("Visible",labels);
            Assert.DoesNotContain("Hidden",labels);
        }
        Assert.Equal(members,unit.Members.Count);
    }
}
