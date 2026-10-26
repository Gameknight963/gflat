using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests.Std.Collections;

public sealed class HashSetRemoveWhereTests
{
    [Fact]
    public void BorrowedOwningKeyPredicateRetainsItsConcreteImmutableSignatureAndDefinition()
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        const string text = """
            using std; using std::Text; using std::Collections;
            bool Temporary(immutable(String)* key) => key.StartsWith("tmp:");
            void Use(ref HashSet<String> names) { nuint removed = names.RemoveWhere(Temporary); }
            """;
        var source = new SourceFile(Path.GetFullPath("hashset-remove-where-editor.gf"), text);
        sources.Add(source);
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, sources);
        int call = text.IndexOf("RemoveWhere(", StringComparison.Ordinal);
        var completions = Json(model.Completion(source.Path, call)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(completions, item => item.GetProperty("label").GetString() == "RemoveWhere");
        Assert.DoesNotContain(completions, item => item.GetProperty("label").GetString() == "RemoveKeysWhere");
        string hover = Json(model.Hover(source.Path, call)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("nuint", hover);
        Assert.Contains("immutable(String)*", hover);
        Assert.DoesNotContain('$', hover);
        var definition = model.Definition(source.Path, call);
        Assert.NotNull(definition);
        var setSource = sources.Single(file => Path.GetFileName(file.Path) == "HashSet.gf");
        int declaration = setSource.Text.IndexOf("public nuint RemoveWhere(", StringComparison.Ordinal) + "public nuint ".Length;
        Assert.True(declaration >= "public nuint ".Length);
        var expected = model.Definition(setSource.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, definition);
        var help = Json(model.SignatureHelp(source.Path, call + "RemoveWhere(".Length));
        Assert.Equal(0, help.GetProperty("activeParameter").GetInt32());
        string label = Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString()!;
        Assert.Equal("public nuint std::Collections::HashSet<std::Text::String>::RemoveWhere(bool(immutable(std::Text::String)*)* predicate)", label);
        Assert.Contains("predicate", label);
        Assert.DoesNotContain('$', label);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
