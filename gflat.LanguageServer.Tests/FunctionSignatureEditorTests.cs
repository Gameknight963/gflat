using gflat.diagnostics;
using gflat.LanguageServer;
using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class FunctionSignatureEditorTests
{
    [Fact]
    public void SignatureAliasHoverPreservesSignatureSpelling()
    {
        const string text = "alias Transform = int(int); int Double(int value) => value * 2; void Test() { Transform* callback = Double; }";
        var source = new SourceFile(Path.GetFullPath("function-signature.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var hover = model.Hover(source.Path, text.IndexOf("Transform", StringComparison.Ordinal));
        Assert.NotNull(hover);
        string description = JsonSerializer.SerializeToElement(hover, Protocol.JsonOptions).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int(int)", description);
        Assert.DoesNotContain("Size:", description);
        Assert.NotNull(model.Definition(source.Path, text.LastIndexOf("Double", StringComparison.Ordinal)));
    }
}
