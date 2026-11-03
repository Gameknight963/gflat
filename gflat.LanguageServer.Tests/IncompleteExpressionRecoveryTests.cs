using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class IncompleteExpressionRecoveryTests
{
    [Theory]
    [InlineData("Take(item., );")]
    [InlineData("Take(, item.Value);")]
    [InlineData("int pending = item.Value + ;")]
    [InlineData("item.Value = ;")]
    [InlineData("Take(item.Value\n")]
    public void UnfinishedExpressionsPreserveFollowingStatements(string unfinished)
    {
        var source = new SourceFile(Path.GetFullPath("unfinished-expression.gf"), """
            struct Item { public int Value; }
            void Take(int first, int second = 0) {}
            int Work() {
                Item item = default;
            """ + unfinished + "\nint later = 42; return later; }\nint Healthy() => 7;");
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int use = source.Text.LastIndexOf("later", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, use));
        Assert.NotNull(model.Definition(source.Path, use));
        var items = JsonSerializer.SerializeToElement(model.Completion(source.Path, use), Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray();
        Assert.Contains(items, item => item.GetProperty("label").GetString() == "item");
        Assert.NotNull(model.Hover(source.Path, source.Text.IndexOf("Healthy", StringComparison.Ordinal)));
    }
}
