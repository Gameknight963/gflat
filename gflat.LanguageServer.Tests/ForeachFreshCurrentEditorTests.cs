using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ForeachFreshCurrentEditorTests
{
    [Fact]
    public void FreshOwningIterationUsesTheDeclaredLoopSymbolAndItsMemberNavigation()
    {
        const string text = """
            struct Item { public int Id; public Item(int id){Id=id;} ~Item(){} }
            struct Cursor { public bool MoveNext()=>false; public Item Current { get=>new Item(1); } }
            struct Source { public Cursor GetEnumerator()=>new Cursor(); }
            void Probe(){foreach(Item value in new Source()){int id=value.Id;}}
            """;
        var source = new SourceFile(Path.GetFullPath("foreach-fresh-current-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int use = text.IndexOf("value.Id", StringComparison.Ordinal);
        Assert.True(use >= 0);
        string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Item value", hover);
        int declaration = text.IndexOf("Item value in", StringComparison.Ordinal) + "Item ".Length;
        var expected = model.Definition(source.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));

        int member = use + "value.".Length;
        Assert.Contains(Json(model.Completion(source.Path, member)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "Id");
        int field = text.IndexOf("int Id", StringComparison.Ordinal) + "int ".Length;
        var fieldDefinition = model.Definition(source.Path, field);
        Assert.NotNull(fieldDefinition);
        Assert.Equal(fieldDefinition, model.Definition(source.Path, member));
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
