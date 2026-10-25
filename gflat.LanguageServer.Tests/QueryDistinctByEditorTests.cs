using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QueryDistinctByEditorTests
{
    [Fact]
    public void WholeRowsAndPrimitiveKeysInferConcreteQueryAndNavigateActualAdapter()
    {
        const string text = """
            using std;
            struct Row { public int Id; public int Payload; }
            int Key(Row value) => value.Id;
            void Observe() {
                Row[2] data = default(Row[2]);
                Span<Row> rows = new(data, 2);
                var query = rows.AsEnumerable().DistinctBy(Key);
                var cursor = query.GetEnumerator();
                if (cursor.MoveNext()) { Row value = cursor.Current; }
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("query-distinct-by-editor.gf"), text);
        files.Add(source);
        SourceFile extension = files.Single(file => Path.GetFileName(file.Path) == "EnumerableDistinctBy.gf");
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "DistinctBySource.gf");
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int call = text.IndexOf("DistinctBy(", StringComparison.Ordinal);
        Assert.True(call >= 0);
        Assert.Single(Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "DistinctBy");
        string hover = Hover(model, source.Path, call);
        Assert.Contains("DistinctBy", hover);
        Assert.Contains("Enumerable<Row", hover.Replace(" ", ""));
        Assert.Contains("int", hover);
        Assert.DoesNotContain('$', hover);
        AssertDefinition(model, source, call, extension, "DistinctBy<T, K : IHashable, S, E>(", "");

        var help = Json(model.SignatureHelp(source.Path, call + "DistinctBy(".Length));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(1, signatures[0].GetProperty("parameters").GetArrayLength());
        string label = signatures[0].GetProperty("label").GetString()!;
        Assert.Contains("DistinctBy", label);
        Assert.Contains("selector", label);
        Assert.Contains("Row", label);
        Assert.Contains("int", label);
        Assert.DoesNotContain('$', label);

        int current = text.IndexOf("cursor.Current", StringComparison.Ordinal);
        Assert.True(current >= 0);
        current += "cursor.".Length;
        Assert.Contains("Row", Hover(model, source.Path, current));
        AssertDefinition(model, source, current, library, "public T Current", "public T ");
    }

    private static string Hover(EditorModel model, string path, int offset)
        => Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;

    private static void AssertDefinition(EditorModel model, SourceFile source, int use, SourceFile library, string declaration, string beforeName)
    {
        int start = library.Text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing declaration " + declaration);
        var expected = model.Definition(library.Path, start + beforeName.Length);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
