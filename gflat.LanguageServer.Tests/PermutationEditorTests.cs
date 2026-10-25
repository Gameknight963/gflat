using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PermutationEditorTests
{
    [Fact]
    public void ExplicitComparersAndBooleanResultsNavigateBothArrangementOperations()
    {
        const string text = """
            using std;
            int Compare(readonly(int)* a, readonly(int)* b)
            {
                if (*a < *b) return -1;
                if (*a > *b) return 1;
                return 0;
            }
            void Observe()
            {
                int[3] data = [1, 2, 3];
                Span<int> values = new(data, 3);
                bool next = NextPermutation<int>(values, Compare);
                bool previous = PreviousPermutation<int>(values, Compare);
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "Permutations.gf");
        var source = new SourceFile(Path.GetFullPath("permutation-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (string name in new[] { "NextPermutation", "PreviousPermutation" })
        {
            int use = text.IndexOf(name + "<int>(", StringComparison.Ordinal);
            Assert.True(use >= 0);
            Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString() == name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(name + "<int>", hover);
            Assert.Contains("Span<int>", hover);
            Assert.Contains("bool", hover);
            Assert.Contains("compare", hover);
            Assert.DoesNotContain('$', hover);
            string anchor = "public bool " + name + "<T>(";
            int declaration = library.Text.IndexOf(anchor, StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + "public bool ".Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            int argument = text.IndexOf("values,", use, StringComparison.Ordinal);
            Assert.True(argument > use);
            var help = Json(model.SignatureHelp(source.Path, argument + "values,".Length));
            Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());
            Assert.Contains("compare", signature.GetProperty("label").GetString());
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
