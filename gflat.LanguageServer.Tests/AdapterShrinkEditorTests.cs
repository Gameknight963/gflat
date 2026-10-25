using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class AdapterShrinkEditorTests
{
    [Fact]
    public void BothCapacityAdaptersNavigateToTheirOwnPublicForwarders()
    {
        const string text = """
            using std; using std::Collections;
            int Compare(immutable(int)* first, immutable(int)* second) {
                if (*first < *second) return -1;
                if (*first > *second) return 1;
                return 0;
            }
            void Observe() {
                Stack<int> stack = new();
                PriorityQueue<int> queue = new(Compare);
                stack.ShrinkToFit();
                queue.ShrinkToFit();
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("adapter-shrink-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var pair in new[] { (Receiver: "stack", File: "Stack.gf"), (Receiver: "queue", File: "PriorityQueue.gf") })
        {
            int use = text.IndexOf(pair.Receiver + ".ShrinkToFit", StringComparison.Ordinal) + pair.Receiver.Length + 1;
            Assert.True(use > pair.Receiver.Length);
            Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString() == "ShrinkToFit");
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("void", hover);
            Assert.Contains("ShrinkToFit()", hover);
            Assert.DoesNotContain("nothrow", hover);
            SourceFile library = files.Single(file => Path.GetFileName(file.Path) == pair.File);
            int declaration = library.Text.IndexOf("public void ShrinkToFit()", StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + "public void ".Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            var help = Json(model.SignatureHelp(source.Path, use + "ShrinkToFit(".Length));
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Empty(signature.GetProperty("parameters").EnumerateArray());
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
