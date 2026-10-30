using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DisjointSetEditorTests
{
    [Fact]
    public void OwningGroupsExposeImmutableQueriesAndMutableUnionWithTheirSourceDefinitions()
    {
        const string text = "using Standard; using Standard::Collections;void Probe(){DisjointSet groups=new(8);bool changed=groups.Union(1,2);immutable(DisjointSet)* view=&groups;nuint root=view.Find(1);nuint size=view.SizeOf(2);bool same=view.Connected(1,2);nuint count=view.SetCount;}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "DisjointSet.gf");
        var source = new SourceFile(Path.GetFullPath("disjoint-set-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var row in new[] { (Name: "Union", Type: "bool", Immutable: false), (Name: "Find", Type: "nuint", Immutable: true), (Name: "SizeOf", Type: "nuint", Immutable: true), (Name: "Connected", Type: "bool", Immutable: true) })
        {
            int use = text.IndexOf("." + row.Name + "(", StringComparison.Ordinal) + 1;
            var completion = Json(model.Completion(source.Path, use)).GetProperty("items");
            Assert.Single(completion.EnumerateArray(), item => item.GetProperty("label").GetString() == row.Name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Type, hover);
            Assert.Contains(row.Name + "(", hover);
            Assert.DoesNotContain('$', hover);
            if (row.Immutable) Assert.Contains("immutable", hover);
            int declaration = library.Text.IndexOf(row.Type + " " + row.Name + "(", StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration + row.Type.Length + 1);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
        }
        int argument = text.IndexOf("1,2)", StringComparison.Ordinal) + 2;
        var help = Json(model.SignatureHelp(source.Path, argument));
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("nuint second", signature.GetProperty("label").GetString()!);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
