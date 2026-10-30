using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QueryConcatEditorTests
{
    [Fact]
    public void DifferentSourcesSelectConcreteConcatAndNavigateItsValueReturningCursor()
    {
        const string text = """
            using Standard; using Standard::Memory; using Standard::Queries;
            void Observe() {
                int[2] data = [1, 2];
                Span<int> first = new(data, 2);
                IntRange second = new(3, 2);
                var query = first.AsEnumerable().Concat(second.AsEnumerable());
                var cursor = query.GetEnumerator();
                if (cursor.MoveNext()) { int value = cursor.Current; }
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("query-concat-editor.gf"), text);
        files.Add(source);
        SourceFile extension = files.Single(file => Path.GetFileName(file.Path) == "enumerable_concat.gf");
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "ConcatSource.gf");
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);

        int call = text.IndexOf("Concat(", StringComparison.Ordinal);
        Assert.True(call >= 0);
        var items = Json(model.Completion(source.Path, call)).GetProperty("items");
        Assert.Single(items.EnumerateArray(), item => item.GetProperty("label").GetString() == "Concat");
        string hover = Hover(model, source.Path, call);
        Assert.Contains("Concat", hover);
        Assert.Contains("Enumerable<int", hover.Replace(" ", ""));
        Assert.Contains("Span<int>", hover.Replace(" ", ""));
        Assert.Contains("IntRange", hover);
        Assert.DoesNotContain('$', hover);
        AssertDefinition(model, source, call, extension, "Concat<T, LS, LE, RS, RE>(", "");

        int argument = text.IndexOf("second.AsEnumerable()", call, StringComparison.Ordinal);
        Assert.True(argument >= 0);
        var help = Json(model.SignatureHelp(source.Path, argument));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        var signatures = help.GetProperty("signatures");
        Assert.Equal(1, signatures.GetArrayLength());
        Assert.Equal(1, signatures[0].GetProperty("parameters").GetArrayLength());
        string label = signatures[0].GetProperty("label").GetString()!;
        Assert.Contains("Concat", label);
        Assert.Contains("IntRange", label);
        Assert.DoesNotContain('$', label);

        int current = text.IndexOf("cursor.Current", StringComparison.Ordinal);
        Assert.True(current >= 0);
        current += "cursor.".Length;
        string currentHover = Hover(model, source.Path, current);
        Assert.Contains("int", currentHover);
        Assert.Contains("Current", currentHover);
        Assert.DoesNotContain('$', currentHover);
        AssertDefinition(model, source, current, library, "public T Current", "public T ");
    }

    private static string Hover(EditorModel model, string path, int offset)
        => Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;

    private static void AssertDefinition(EditorModel model, SourceFile source, int use, SourceFile library, string declaration, string beforeName)
    {
        int start = declaration.StartsWith("Concat<",StringComparison.Ordinal) ? EditorSourceDeclarations.ExtensionMethod(library,"Concat") : library.Text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing declaration " + declaration);
        var expected = model.Definition(library.Path, start + beforeName.Length);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
