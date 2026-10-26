using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class AsciiHashingEditorTests
{
    [Fact]
    public void CountedHashUsesTheActualAsciiNamespaceDeclarationAndNoThrowSignature()
    {
        const string text = "nuint Probe(readonly(char)* data,nuint count)nothrow=>std::Text::Ascii::HashIgnoreCase(data,count);";
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        var library = files.Single(file => Path.GetFileName(file.Path) == "ascii.gf");
        var source = new SourceFile(Path.GetFullPath("ascii-hashing-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int use = text.IndexOf("HashIgnoreCase", StringComparison.Ordinal);

        var items = Json(model.Completion(source.Path, use)).GetProperty("items");
        Assert.Single(items.EnumerateArray(), item => item.GetProperty("label").GetString() == "HashIgnoreCase");
        string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("nuint", hover);
        Assert.Contains("HashIgnoreCase", hover);
        Assert.Contains("readonly(char)* data", hover);
        Assert.Contains("nuint count", hover);
        Assert.Contains("nothrow", hover);
        Assert.DoesNotContain('$', hover);

        const string anchor = "nuint HashIgnoreCase(readonly(char)* data, nuint count)";
        int declaration = library.Text.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        declaration += "nuint ".Length;
        var expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));

        int namespaceUse = text.IndexOf("Ascii::", StringComparison.Ordinal);
        int namespaceDeclaration = library.Text.IndexOf("Ascii;", StringComparison.Ordinal);
        Assert.True(namespaceDeclaration >= 0);
        var namespaceTarget = model.Definition(library.Path, namespaceDeclaration);
        Assert.NotNull(namespaceTarget);
        Assert.Equal(namespaceTarget, model.Definition(source.Path, namespaceUse));

        var help = Json(model.SignatureHelp(source.Path, use + "HashIgnoreCase(data,".Length));
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());
        string label = signature.GetProperty("label").GetString()!;
        Assert.Contains("readonly(char)* data", label);
        Assert.Contains("nuint count", label);
        Assert.Contains("nothrow", label);
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
