using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ParameterSpliceEditorTests
{
    [Fact]
    public void GeneratedParametersAppearInHoverAndSignatureHelp()
    {
        const string text = "int Add(int left,int right)=>left+right; int Forward([:reflect(Add).Parameters:])=>Add([:reflect(Add).Parameters:]); int Main()=>Forward(20,22);";
        var source = new SourceFile(Path.GetFullPath("parameter-splice-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true, NoBuiltins = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int use = text.LastIndexOf("Forward(", StringComparison.Ordinal);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, use), Protocol.JsonOptions);
        Assert.Contains("int Forward(int left, int right)", hover.GetProperty("contents").GetProperty("value").GetString());
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, use + 8), Protocol.JsonOptions);
        Assert.Equal(2, signature.GetProperty("signatures")[0].GetProperty("parameters").GetArrayLength());
    }
}
