using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QueryIndexedSelectEditorTests
{
    [Fact]
    public void CallbackAritySelectsConcreteProjectionAndNavigatesItsActualAdapter()
    {
        const string text = """
            using std; using std::Queries;
            void Observe()
            {
                int[2] data = [4, 8];
                Span<int> values = new(data, 2);
                var plain = values.AsEnumerable().Select((int value) => value + 1);
                var indexed = values.AsEnumerable().Select((int value, nuint index) => value + (int)index);
                var cursor = indexed.GetEnumerator();
                if (cursor.MoveNext()) { int result = cursor.Current; }
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("query-indexed-select-editor.gf"), text);
        files.Add(source);
        SourceFile extension = files.Single(file => Path.GetFileName(file.Path) == "EnumerableIndexedSelect.gf");
        SourceFile ordinary = files.Single(file => Path.GetFileName(file.Path) == "EnumerableExtensions.gf");
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int plain = text.IndexOf("Select(", StringComparison.Ordinal);
        int indexed = text.IndexOf("Select(", plain + 1, StringComparison.Ordinal);
        Assert.True(plain >= 0 && indexed > plain);
        Assert.Single(Json(model.Completion(source.Path, indexed)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "Select");
        string hover = Json(model.Hover(source.Path, indexed)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("IndexedSelectSource<int, int", hover);
        Assert.Contains("nuint", hover);
        Assert.DoesNotContain('$', hover);
        int declaration = extension.Text.IndexOf("Select<T, U, S, E>(", StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        var expected = model.Definition(extension.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, indexed));
        declaration = ordinary.Text.IndexOf("Select<T, U, S, E>(", StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        expected = model.Definition(ordinary.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, plain));
        var help = Json(model.SignatureHelp(source.Path, indexed + "Select(".Length));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(2, signatures.GetArrayLength());
        string label = signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString()!;
        Assert.Contains("nuint", label);
        Assert.Contains("selector", label);
        Assert.DoesNotContain('$', label);
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
