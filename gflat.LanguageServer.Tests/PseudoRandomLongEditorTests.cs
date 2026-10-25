using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PseudoRandomLongEditorTests
{
    [Fact]
    public void SignedDrawOverloadsHaveReadableLongSignaturesAndActualDeclarations()
    {
        const string text = "using std;void Probe(){PseudoRandom random=new(42ul);long bits=random.NextLong();long upper=random.NextLong(10l);long interval=random.NextLong(-10l,10l);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "PseudoRandom.gf");
        var source = new SourceFile(Path.GetFullPath("pseudo-random-long-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);

        foreach (var row in new[] {
            (Call: ".NextLong()", Arity: 0, Anchor: "public long NextLong()", Prefix: "NextLong("),
            (Call: ".NextLong(10l)", Arity: 1, Anchor: "public long NextLong(long exclusiveMax)", Prefix: "NextLong("),
            (Call: ".NextLong(-10l,10l)", Arity: 2, Anchor: "public long NextLong(long inclusiveMin, long exclusiveMax)", Prefix: "NextLong(-10l,")
        })
        {
            int use = text.IndexOf(row.Call, StringComparison.Ordinal) + 1;
            Assert.True(use > 0);
            var items = Json(model.Completion(source.Path, use)).GetProperty("items");
            Assert.Single(items.EnumerateArray(), item => item.GetProperty("label").GetString() == "NextLong");
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("long", hover);
            Assert.Contains("NextLong", hover);
            Assert.DoesNotContain('$', hover);
            if (row.Arity == 0) Assert.Contains("nothrow", hover);

            int declaration = library.Text.IndexOf(row.Anchor, StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + "public long ".Length);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));

            var help = Json(model.SignatureHelp(source.Path, use + row.Prefix.Length));
            Assert.Equal(Math.Max(0, row.Arity - 1), help.GetProperty("activeParameter").GetInt32());
            var signatures = help.GetProperty("signatures");
            Assert.Equal(3, signatures.GetArrayLength());
            var selected = signatures[help.GetProperty("activeSignature").GetInt32()];
            Assert.Equal(row.Arity, selected.GetProperty("parameters").GetArrayLength());
            string label = selected.GetProperty("label").GetString()!;
            Assert.Contains("long", label);
            if (row.Arity > 0) Assert.Contains("long exclusiveMax", label);
            if (row.Arity == 2) Assert.Contains("long inclusiveMin", label);
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
