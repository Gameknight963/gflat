using gflat.diagnostics;
using gflat.LanguageServer;
using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class StaticClassEditorTests
{
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
