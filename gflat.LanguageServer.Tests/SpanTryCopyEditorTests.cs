using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class SpanTryCopyEditorTests
{
    [Fact]
    public void ImmutableSourceSelectsConcreteTypedCopyAndNavigatesActualExtension()
    {
        const string text = """
            using std; using std::Memory; using std::Queries; using std::Collections;
            void Observe() {
                int[2] input = [1, 2]; int[3] output = [3, 4, 5];
                Span<int> source = new(input, 2);
                Span<int> destination = new(output, 3);
                bool copied = source.AsImmutable().TryCopyTo(destination);
            }
            """;
        string root = Path.Combine(AppContext.BaseDirectory, "StandardLibrary", "core");
        string[] names = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Memory/Span.gf", "Memory/span_copy.gf"];
        var files = names.Select(name => Path.Combine(root, name))
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("span-try-copy-editor.gf"), text);
        files.Add(source);
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "span_copy.gf");
        var snapshot = new AnalysisSnapshot(files);
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int call = text.IndexOf("TryCopyTo(", StringComparison.Ordinal);
        Assert.True(call >= 0);
        Assert.Single(Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "TryCopyTo");
        string hover = Json(model.Hover(source.Path, call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("bool", hover);
        Assert.Contains("TryCopyTo", hover);
        Assert.Contains("Span<int>", hover.Replace(" ", ""));
        Assert.DoesNotContain('$', hover);
        int declaration = library.Text.IndexOf("TryCopyTo<T>(", StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        var expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, call));
        var help = Json(model.SignatureHelp(source.Path, call + "TryCopyTo(".Length));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(1, signatures[0].GetProperty("parameters").GetArrayLength());
        string label = signatures[0].GetProperty("label").GetString()!;
        Assert.Contains("TryCopyTo", label);
        Assert.Contains("destination", label);
        Assert.Contains("Span<int>", label.Replace(" ", ""));
        Assert.DoesNotContain('$', label);
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
