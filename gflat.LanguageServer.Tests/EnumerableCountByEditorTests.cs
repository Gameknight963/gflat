using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class EnumerableCountByEditorTests
{
    [Fact]
    public void PrimitiveAndOwningKeysInferAndNavigateTheCountBySelector()
    {
        const string text = "using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;int Key(int value)=>value%3;String Text(int value)=>value.ToString();void Probe(){int[3] data=[4,2,1];Span<int> values=new(data,3);HashMap<int,nuint> numbers=values.AsEnumerable().CountBy(Key);HashMap<String,nuint> words=values.AsEnumerable().CountBy(Text);}";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "enumerable_count_by.gf");
        var source = new SourceFile(Path.GetFullPath("count-by-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        var expected = model.Definition(library.Path, EditorSourceDeclarations.ExtensionMethod(library,"CountBy"));
        Assert.NotNull(expected);
        foreach (string selector in new[] { "Key", "Text" })
        {
            int use = text.IndexOf("CountBy(" + selector, StringComparison.Ordinal);
            Assert.True(use >= 0);
            var completion = Json(model.Completion(source.Path, use)).GetProperty("items");
            Assert.Contains(completion.EnumerateArray(), item => item.GetProperty("label").GetString() == "CountBy");
            Assert.Equal(expected, model.Definition(source.Path, use));
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("HashMap", hover);
            Assert.Contains("nuint", hover);
            Assert.Contains(selector == "Key" ? "int" : "String", hover);
            Assert.DoesNotContain('$', hover);
            var help = Json(model.SignatureHelp(source.Path, use + "CountBy(".Length));
            var signature = help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()];
            Assert.Single(signature.GetProperty("parameters").EnumerateArray());
        }
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
