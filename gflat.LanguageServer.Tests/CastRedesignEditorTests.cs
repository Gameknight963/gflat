using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Source identities must survive compiler-only intrinsic/conversion lowering.
public sealed class CastRedesignEditorTests
{
    [Fact]
    public void InferredLocalTypeComesFromReinterpretResult()
    {
        const string text="int main(){uint bits=1065353216u;var decoded=bits.reinterpret<float>();return (int)decoded;}";
        var source=new SourceFile(Path.GetFullPath("reinterpret-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        int use=text.LastIndexOf("decoded",StringComparison.Ordinal);
        var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions);
        Assert.Contains("float",hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path,use));
    }

    [Fact]
    public void ExplicitConversionHelpersDoNotLeakSyntheticNamesIntoTypeCompletion()
    {
        const string text="""
            struct Meter{
                public int Value;
                public Meter(int value){Value=value;}
                public static explicit operator int(Meter value)=>value.Value;
                public static explicit operator Meter(int value)=>new Meter(value);
                public static Meter Make()=>new Meter(1);
            }
            int main(){Meter value=(Meter)42;Meter other=Meter::Make();return (int)value;}
            """;
        var source=new SourceFile(Path.GetFullPath("explicit-conversion-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        int use=text.IndexOf("Meter::Make",StringComparison.Ordinal)+"Meter::".Length;
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,use),Protocol.JsonOptions);
        var labels=completion.GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Make",labels);
        Assert.DoesNotContain(labels,label=>label?.Contains("$conversion$",StringComparison.Ordinal)==true);
        int cast=text.IndexOf("(Meter)42",StringComparison.Ordinal)+1;
        var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,cast),Protocol.JsonOptions);
        Assert.DoesNotContain("$conversion$",hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path,cast));
    }
    [Fact]
    public void ContextualDeclarationKeywordsDoNotClassifyOrdinaryLocalsAsKeywords()
    {
        const string text="struct A{public static explicit operator int(A value)=>0;} extension A{public int Marker()=>0;} int main(){int explicit=1;int extension=2;return explicit+extension;}";
        var source=new SourceFile(Path.GetFullPath("contextual-cast-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        var data=JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path),Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(item=>item.GetInt32()).ToArray();
        var kinds=new Dictionary<int,string>();int column=0;
        for(int i=0;i<data.Length;i+=5){Assert.Equal(0,data[i]);column+=data[i+1];kinds[column]=EditorModel.TokenTypes[data[i+3]];}
        Assert.Equal("modifier",kinds[text.IndexOf("explicit operator",StringComparison.Ordinal)]);
        Assert.Equal("modifier",kinds[text.IndexOf("extension A",StringComparison.Ordinal)]);
        Assert.Equal("variable",kinds[text.LastIndexOf("explicit+",StringComparison.Ordinal)]);
        Assert.Equal("variable",kinds[text.LastIndexOf("extension;",StringComparison.Ordinal)]);
        string outline=JsonSerializer.Serialize(model.DocumentSymbols(source.Path),Protocol.JsonOptions);
        Assert.Contains("explicit operator int",outline);
        Assert.DoesNotContain("$conversion$",outline);
    }

}
