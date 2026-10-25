using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EnumerableExtremaByEditorTests
{
    [Fact]
    public void ProjectedExtremaInferKeysAndNavigateBothOverloads()
    {
        const string text = "using std; using std::Queries;int Key(int value)=>value%3;int Compare(immutable(int)* a,immutable(int)* b){if(*a<*b)return -1;if(*a>*b)return 1;return 0;}void Probe(){int[3] data=[4,2,1];Span<int> values=new(data,3);int low=values.AsEnumerable().MinBy(Key);int high=values.AsEnumerable().MaxBy(Key,Compare);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "EnumerableExtremaBy.gf");
        var source = new SourceFile(Path.GetFullPath("extrema-by-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        foreach (var row in new[] { (Name: "MinBy", Arity: 1), (Name: "MaxBy", Arity: 2) })
        {
            int use = text.IndexOf("." + row.Name + "(", StringComparison.Ordinal) + 1;
            var completion = Json(model.Completion(source.Path, use)).GetProperty("items");
            Assert.Contains(completion.EnumerateArray(), item => item.GetProperty("label").GetString() == row.Name);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("int", hover);
            Assert.Contains(row.Name, hover);
            Assert.DoesNotContain('$', hover);
            int declaration = row.Arity == 1
                ? library.Text.IndexOf("MinBy<T, K :", StringComparison.Ordinal)
                : library.Text.IndexOf("MaxBy<T, K,", StringComparison.Ordinal);
            Assert.True(declaration >= 0);
            var expected = model.Definition(library.Path, declaration);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            int argument = use + row.Name.Length + 1;
            if (row.Arity == 2) argument += "Key,".Length;
            var help = Json(model.SignatureHelp(source.Path, argument));
            Assert.Equal(row.Arity - 1, help.GetProperty("activeParameter").GetInt32());
            Assert.Contains(help.GetProperty("signatures").EnumerateArray(),
                signature => signature.GetProperty("parameters").GetArrayLength() == row.Arity);
        }
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
