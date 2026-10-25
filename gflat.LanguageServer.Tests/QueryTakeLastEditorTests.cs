using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QueryTakeLastEditorTests
{
    [Fact]
    public void CountedTailInfersConcreteElementAndNavigatesItsOwnedCursor()
    {
        const string text = """
            using std; using std::Queries;
            void Observe() {
                int[3] data = [1, 2, 3];
                Span<int> values = new(data, 3);
                var query = values.AsEnumerable().TakeLast(2);
                var cursor = query.GetEnumerator();
                if (cursor.MoveNext()) { int value = cursor.Current; }
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("query-take-last-editor.gf"), text);
        files.Add(source);
        SourceFile extension = files.Single(file => Path.GetFileName(file.Path) == "EnumerableTakeLast.gf");
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "TakeLastSource.gf");
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int call = text.IndexOf("TakeLast(", StringComparison.Ordinal);
        Assert.True(call >= 0);
        Assert.Single(Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "TakeLast");
        string hover = Json(model.Hover(source.Path, call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("TakeLast", hover);
        Assert.Contains("Enumerable<int", hover.Replace(" ", ""));
        Assert.DoesNotContain('$', hover);
        AssertDefinition(model, source, call, extension, "TakeLast<T, S, E>(", "");
        var help = Json(model.SignatureHelp(source.Path, call + "TakeLast(".Length));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(1, signatures[0].GetProperty("parameters").GetArrayLength());
        string label = signatures[0].GetProperty("label").GetString()!;
        Assert.Contains("nuint count", label);
        Assert.DoesNotContain('$', label);
        int current = text.IndexOf("cursor.Current", StringComparison.Ordinal) + "cursor.".Length;
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
