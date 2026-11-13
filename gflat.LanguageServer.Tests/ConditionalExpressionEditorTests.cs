using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ConditionalExpressionEditorTests
{
    [Fact]
    public void InferredResultAndBranchSymbolsRemainAvailableToTheEditor()
    {
        const string text = "int main(){int first=4;int second=9;var selected=true?first:second;return selected;}";
        var source = new SourceFile(Path.GetFullPath("conditional-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true, NoBuiltins = true });
        Assert.DoesNotContain(snapshot.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        foreach (string name in new[] { "first", "second", "selected" })
        {
            int use = text.LastIndexOf(name, StringComparison.Ordinal);
            var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, use), Protocol.JsonOptions);
            Assert.Contains("int", hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.NotNull(model.Definition(source.Path, use));
        }
    }
}
