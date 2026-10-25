using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BitSetSearchEditorTests
{
    [Fact]
    public void MatchingBitSearchHasItsImmutableObserverAndOptionalStartSignature()
    {
        const string text = "using std; using std::Collections;void Observe(immutable(BitSet)* bits){nuint first=bits.FindIndex(false);nuint next=bits.FindIndex(true,32);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "BitSet.gf");
        var source = new SourceFile(Path.GetFullPath("bitset-search-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);

        foreach (var row in new[] { (Call: ".FindIndex(false)", Prefix: "FindIndex(", Active: 0),
            (Call: ".FindIndex(true,32)", Prefix: "FindIndex(true,", Active: 1) })
        {
            int use = text.IndexOf(row.Call, StringComparison.Ordinal) + 1;
            Assert.True(use > 0);
            var items = Json(model.Completion(source.Path, use)).GetProperty("items");
            Assert.Single(items.EnumerateArray(), item => item.GetProperty("label").GetString() == "FindIndex");
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("immutable nuint", hover);
            Assert.Contains("bool value", hover);
            Assert.Contains("nuint start = 0", hover);
            Assert.DoesNotContain('$', hover);
            const string anchor = "public immutable nuint FindIndex(bool value, nuint start = 0)";
            int declaration = library.Text.IndexOf(anchor, StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + "public immutable nuint ".Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            var help = Json(model.SignatureHelp(source.Path, use + row.Prefix.Length));
            Assert.Equal(row.Active, help.GetProperty("activeParameter").GetInt32());
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());
            Assert.Contains("nuint start = 0", signature.GetProperty("label").GetString());
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
