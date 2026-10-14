using gflat.LanguageServer;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NonCallableExpressionEditorTests
{
    [Fact]
    public void InvalidValueCallDoesNotPreventAnalysisOfLaterFunctions()
    {
        var source = new SourceFile(Path.GetFullPath("invalid-call.gf"), """
            int Bad() { 1(2); return 0; }
            int Good() => 42;
            int main() => Good();
            """);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int call = source.Text.LastIndexOf("Good", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, call));
        Assert.NotNull(model.Definition(source.Path, call));
    }
}
