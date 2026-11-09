using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class SpanToVectorEditorTests
{
    [Fact]
    public void SpanCopiesHaveTypedCompletionSignaturesAndNavigation()
    {
        const string text = """
            using Standard::Collections;
            using Standard::Memory;

            int Read()
            {
                int[2] values = [7, 9];
                Span<int> view = new(values, 2);
                Vector<int> copy = view.AsImmutable().ToVector();
                return copy[0];
            }
            """;
        var source = new SourceFile(Path.GetFullPath("span_to_vector_editor.gf"), text);
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/span_to_vector.gf")).Append(source).ToArray();
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, sources);
        int method = text.IndexOf("ToVector", StringComparison.Ordinal);
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "ToVector");
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, method + "ToVector(".Length), Protocol.JsonOptions);
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Contains("Vector<int>", signature.GetProperty("label").GetString());
        Assert.Empty(signature.GetProperty("parameters").EnumerateArray());
        Assert.Contains("span_to_vector.gf", JsonSerializer.Serialize(model.Definition(source.Path, method)));
    }
}
