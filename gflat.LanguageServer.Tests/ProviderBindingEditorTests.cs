using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ProviderBindingEditorTests
{
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);

    [Theory]
    [InlineData("Selected")]
    [InlineData("Another")]
    [InlineData("Api::Selected")]
    public void ProviderCompletionOffersOnlyTheContract(string qualifier)
    {
        string ns = qualifier.Contains("::") ? "namespace Api {" : "";
        string close = ns.Length == 0 ? "" : "}";
        string text = $$"""
            {{ns}}
            interface IProvider { static int Read(); }
            static class Answer : IProvider {
                public static int Read() => 42;
                public static int Extra() => 0;
            }
            weak alias Selected : IProvider = Answer;
            alias Another = Selected;
            {{close}}
            int main() => {{qualifier}}::Read();
            """;
        var source = new SourceFile(Path.GetFullPath("provider-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        var model = new EditorModel(snapshot, [source]);
        int read = text.LastIndexOf("Read", StringComparison.Ordinal);
        var items = Json(model.Completion(source.Path, read)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, i => i.GetProperty("label").GetString() == "Read");
        Assert.DoesNotContain(items, i => i.GetProperty("label").GetString() == "Extra");
        Assert.NotNull(model.Hover(source.Path, read));
        Assert.NotNull(model.Definition(source.Path, read));
    }

    [Fact]
    public void MissingImplementationRetainsExpectedAliasHoverAndContractCompletion()
    {
        const string text = "interface IProvider { static int Read(); } expect alias Selected : IProvider; int main() => Selected::Read();";
        var source = new SourceFile(Path.GetFullPath("missing-provider.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.NotEmpty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int use = text.LastIndexOf("Selected", StringComparison.Ordinal);
        Assert.Contains("expect alias Selected", Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString());
        var items = Json(model.Completion(source.Path, text.LastIndexOf("Read", StringComparison.Ordinal))).GetProperty("items").EnumerateArray();
        Assert.Contains(items, i => i.GetProperty("label").GetString() == "Read");
    }

    [Fact]
    public void QualifiedFunctionReplacementNavigatesToItsActualName()
    {
        const string text = "namespace Api { public expect int Read(); } replace int Api::Read() => 42; int main() => Api::Read();";
        var source = new SourceFile(Path.GetFullPath("qualified-provider.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        int call = text.LastIndexOf("Read", StringComparison.Ordinal);
        var definition = Json(model.Definition(source.Path, call));
        Assert.NotEqual(JsonValueKind.Null, definition.ValueKind);
        Assert.NotNull(model.Hover(source.Path, call));
    }
}
