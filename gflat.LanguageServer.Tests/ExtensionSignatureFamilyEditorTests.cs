using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ExtensionSignatureFamilyEditorTests
{
    [Fact]
    public void DotSignatureFamilyFiltersReceiversButFreeCallRetainsDeclaredOverloads()
    {
        const string library="""
            namespace Extras {
                int Pick(int value,int amount)=>amount; int Pick(int value,long amount)=>0;
                int Pick(bool value,int amount)=>amount; int Pick(bool value,long amount)=>0;
                extension int{public int Pick(int amount)=>amount;}
                extension int{public int Pick(long amount)=>0;}
                extension bool{public int Pick(int amount)=>amount;}
                extension bool{public int Pick(long amount)=>0;}
            }
            """;
        const string text="using Extras; int main(){int value=1;return value.Pick(1)+Extras::Pick(value,1);}";
        var (model,source)=Analyze(text,library);
        var dot=Help(model,source,text.IndexOf("Pick(",StringComparison.Ordinal)+5);
        Assert.Equal(2,dot.Length);
        Assert.All(dot,s=>Assert.Single(s.GetProperty("parameters").EnumerateArray()));
        var free=Help(model,source,text.LastIndexOf("Pick(",StringComparison.Ordinal)+5);
        Assert.Equal(4,free.Length);
        Assert.All(free,s=>Assert.Equal(2,s.GetProperty("parameters").GetArrayLength()));
    }

    [Fact]
    public void ReceiverBindingsCloseSiblingTypesWithoutInferringTheirLaterArguments()
    {
        const string library="""
            struct Box<T>{public T Value;}
            namespace Extras {
                extension<T> Box<T>{public T Pick(int amount)=>(*this).Value;}
                extension<T> Box<T>{public T Pick<U>(U other,int marker)=>(*this).Value;}
            }
            """;
        const string text="using Extras; int main(){Box<int> value=new();return value.Pick(1);}";
        var (model,source)=Analyze(text,library);
        var signatures=Help(model,source,text.IndexOf("Pick(",StringComparison.Ordinal)+5);
        Assert.Equal(2,signatures.Length);
        string[] labels=signatures.Select(s=>s.GetProperty("label").GetString()!).ToArray();
        Assert.Contains(labels,label=>label.Contains("int amount",StringComparison.Ordinal));
        Assert.Contains(labels,label=>label.Contains("U other",StringComparison.Ordinal));
        Assert.All(labels,label=>Assert.Contains("int Extras::Pick",label));
    }

    private static (EditorModel Model,SourceFile Source) Analyze(string text,string library)
    {
        var source=new SourceFile(Path.GetFullPath("receiver-family-main.gf"),text);
        var extra=new SourceFile(Path.GetFullPath("receiver-family-library.gf"),library);
        var snapshot=new AnalysisSnapshot([source,extra],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        return (new EditorModel(snapshot,[source,extra]),source);
    }

    private static JsonElement[] Help(EditorModel model,SourceFile source,int offset)
        => JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,offset),Protocol.JsonOptions)
            .GetProperty("signatures").EnumerateArray().ToArray();
}
