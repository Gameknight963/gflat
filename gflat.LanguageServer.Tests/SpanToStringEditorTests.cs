using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class SpanToStringEditorTests
{
    [Theory]
    [InlineData("char")]
    [InlineData("readonly(char)")]
    public void TextSpanCopiesHaveCompletionHoverAndDefinition(string element)
    {
        string text = $$"""
            using Standard::Memory;
            using Standard::Text;

            int main()
            {
                char[1] storage = ['x'];
                Span<{{element}}> view = new(storage, 1);
                String copy = view.ToString();
                return 0;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("span-copy-editor.gf"), text);
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Text/span_to_string.gf")).Append(source).ToArray();
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, sources);
        int method = text.IndexOf("ToString", StringComparison.Ordinal);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, method));
        Assert.Contains("String", hover);
        Assert.Contains("ToString", hover);
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "ToString");
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, method + "ToString(".Length), Protocol.JsonOptions);
        Assert.Empty(Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("parameters").EnumerateArray());
        Assert.Contains("span_to_string.gf", JsonSerializer.Serialize(model.Definition(source.Path, method)));
    }
}
