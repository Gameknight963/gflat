using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QuerySkipLastEditorTests
{
    [Fact]
    public void StreamingSuffixSkipInfersConcreteElementAndNavigatesItsBufferedCurrent()
    {
        const string text = """
            using std;
            void Observe() {
                int[3] data = [1, 2, 3];
                Span<int> values = new(data, 3);
                var query = values.AsEnumerable().SkipLast(2);
                var cursor = query.GetEnumerator();
                if (cursor.MoveNext()) { int value = cursor.Current; }
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("query-skip-last-editor.gf"), text);
        files.Add(source);
        SourceFile extension = files.Single(file => Path.GetFileName(file.Path) == "EnumerableSkipLast.gf");
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "SkipLastSource.gf");
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int call = text.IndexOf("SkipLast(", StringComparison.Ordinal);
        Assert.True(call >= 0);
        Assert.Single(Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "SkipLast");
        string hover = Json(model.Hover(source.Path, call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("SkipLast", hover);
        Assert.Contains("Enumerable<int", hover.Replace(" ", ""));
        Assert.DoesNotContain('$', hover);
        AssertDefinition(model, source, call, extension, "SkipLast<T, S, E>(", "");
        var help = Json(model.SignatureHelp(source.Path, call + "SkipLast(".Length));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(1, signatures[0].GetProperty("parameters").GetArrayLength());
        string label = signatures[0].GetProperty("label").GetString()!;
        Assert.Contains("nuint count", label);
        Assert.DoesNotContain('$', label);
        int currentStart = text.IndexOf("cursor.Current", StringComparison.Ordinal);
        Assert.True(currentStart >= 0);
        int current = currentStart + "cursor.".Length;
        string currentHover = Json(model.Hover(source.Path, current)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int", currentHover);
        Assert.Contains("Current", currentHover);
        AssertDefinition(model, source, current, library, "public T Current", "public T ");
    }

    private static void AssertDefinition(EditorModel model, SourceFile source, int use, SourceFile library, string declaration, string prefix)
    {
        int start = library.Text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing declaration " + declaration);
        var expected = model.Definition(library.Path, start + prefix.Length);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
