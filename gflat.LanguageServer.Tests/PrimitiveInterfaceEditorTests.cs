using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PrimitiveInterfaceEditorTests
{
    [Fact]
    public void PrimitiveConstrainedExtensionsHaveCompletionHoverAndNavigation()
    {
        const string text = """
            interface IHash {
                immutable int Hash();
                implementation int { public immutable int Hash() => *this; }
            }
            extension<T : IHash> T{public immutable int Read()=> (*this).Hash();}
            int main() { int value=42; return value.Read(); }
            """;
        var source = new SourceFile(Path.GetFullPath("primitive-extension.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int method = text.LastIndexOf("Read()", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, method));
        Assert.NotNull(model.Definition(source.Path, method));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Read");
    }

    [Fact]
    public void ImplementationBodiesAndConstrainedCallsHaveSemanticInformation()
    {
        const string text = """
            interface IHash {
                immutable int Hash(int extra);
                implementation int { public immutable int Hash(int extra) => *this + extra; }
            }
            int Read<T : IHash>(T n) => n.Hash(2);
            int main() => Read(40);
            """;
        var source = new SourceFile(Path.GetFullPath("primitive-interface.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int method = text.IndexOf("n.Hash", StringComparison.Ordinal) + 2;
        Assert.NotNull(model.Hover(source.Path, method));
        Assert.NotNull(model.Definition(source.Path, method));
        int extra = text.IndexOf("+ extra", StringComparison.Ordinal) + 2;
        Assert.NotNull(model.Hover(source.Path, extra));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Hash");
    }

    [Fact]
    public void AnInvalidImplementationDoesNotEraseFollowingDeclarations()
    {
        const string text = "interface I { int Read(); implementation int {} } struct Healthy { public int Read() => 42; } int main() { Healthy h = new(); return h.Read(); }";
        var source = new SourceFile(Path.GetFullPath("primitive-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int method = text.LastIndexOf("Read()", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, method));
        Assert.NotNull(model.Definition(source.Path, method));
    }
    [Fact]
    public void OptInPrimitiveMethodsKeepSourceNavigationAndContextualKeywordClassification()
    {
        const string text="interface IAdd{immutable int Add(int amount);extension implementation int{public immutable int Add(int amount)=>*this+amount;}}int main(){int value=40;int extension=0;int implementation=0;return value.Add(2)+extension+implementation;}";
        var source=new SourceFile(Path.GetFullPath("primitive-opt-in-editor.gf"),text);
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[source]);
        int use=text.IndexOf("value.Add",StringComparison.Ordinal)+"value.".Length;
        var hover=System.Text.Json.JsonSerializer.SerializeToElement(model.Hover(source.Path,use),Protocol.JsonOptions);
        string signature=hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Add(int amount)",signature);Assert.DoesNotContain('$',signature);
        int declaration=text.IndexOf("Add(int amount)=>",StringComparison.Ordinal);
        Assert.True(declaration>=0);
        Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(declaration,"Add".Length))),
            Assert.IsType<Location>(model.Definition(source.Path,use)));
        var completion=JsonSerializer.SerializeToElement(model.Completion(source.Path,use),Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="Add");
        var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,use+"Add(".Length),Protocol.JsonOptions);
        Assert.Single(help.GetProperty("signatures")[0].GetProperty("parameters").EnumerateArray());
        var data=JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path),Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(item=>item.GetInt32()).ToArray();
        int column=0;var classifications=new Dictionary<int,string>();
        for(int i=0;i<data.Length;i+=5){Assert.Equal(0,data[i]);column+=data[i+1];classifications[column]=EditorModel.TokenTypes[data[i+3]];}
        Assert.Equal("modifier",classifications[text.IndexOf("extension implementation",StringComparison.Ordinal)]);
        Assert.Equal("modifier",classifications[text.IndexOf("implementation int",StringComparison.Ordinal)]);
        Assert.Equal("variable",classifications[text.LastIndexOf("extension+",StringComparison.Ordinal)]);
        Assert.Equal("variable",classifications[text.LastIndexOf("implementation;",StringComparison.Ordinal)]);
    }

}
