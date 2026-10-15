using gflat.diagnostics;
using gflat.LanguageServer;
using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class StaticClassEditorTests
{
    [Fact]
    public void StaticContractCallInGenericBodyNavigatesToContract()
    {
        const string text = "interface IProvider { static int Get(); } int Read<P : IProvider>() => P::Get();";
        var source = new SourceFile(Path.GetFullPath("contract.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Get", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, call));
        Assert.Equal(model.Definition(source.Path, text.IndexOf("Get", StringComparison.Ordinal)), model.Definition(source.Path, call));
    }

    [Fact]
    public void StaticClassHoverAndQualifiedCallsRemainBound()
    {
        const string text = "public static class Numbers { public static int Answer() => 42; } int main() => Numbers::Answer();";
        var source = new SourceFile(Path.GetFullPath("static.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var hover = model.Hover(source.Path, text.LastIndexOf("Numbers", StringComparison.Ordinal));
        Assert.NotNull(hover);
        Assert.Contains("static class Numbers", JsonSerializer.SerializeToElement(hover, Protocol.JsonOptions).GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path, text.LastIndexOf("Answer", StringComparison.Ordinal)));
    }
}
