using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class TargetTypedCreationEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredCreationHasConstructorHoverNavigationAndSignatureHelp(bool generic)
    {
        string owner = generic ? "Box<int>" : "Box";
        string sourceText = "struct Box" + (generic ? "<T>" : "") +
            " { public int Value; public Box(" + (generic ? "T" : "int") + " value) { Value=42; } } " +
            "int main() { " + owner + "* item = new(42); return item.Value; }";
        var source = new SourceFile(Path.GetFullPath("target-creation.gf"), sourceText);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int creation = sourceText.IndexOf("new(", StringComparison.Ordinal);
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, creation), Protocol.JsonOptions);
        Assert.Contains("int value", hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path, creation));
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, creation + 5, visualStudio: true), Protocol.JsonOptions);
        var entry = Assert.Single(signature.GetProperty("signatures").EnumerateArray());
        Assert.Contains("int value", entry.GetProperty("label").GetString());
        Assert.True(entry.TryGetProperty("_vs_colorizedLabel", out _));
        Assert.NotNull(model.Hover(source.Path, sourceText.LastIndexOf("Value", StringComparison.Ordinal)));
    }

    [Fact]
    public void ConstructorCandidatesRemainAvailableWhileArgumentsAreMissing()
    {
        const string text = "struct Box { public Box(int value) {} } int main() { Box value = new(); return 0; }";
        var source = new SourceFile(Path.GetFullPath("target-creation-recovery.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, text.IndexOf("new(", StringComparison.Ordinal) + 4), Protocol.JsonOptions);
        Assert.Contains("int value", help.GetProperty("signatures")[0].GetProperty("label").GetString());
    }
}
