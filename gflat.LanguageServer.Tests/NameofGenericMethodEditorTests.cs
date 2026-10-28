using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class NameofGenericMethodEditorTests
{
    [Theory]
    [InlineData("class", false)]
    [InlineData("class", true)]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    public void GenericMethodNamesKeepNavigationAndConstantPreviewWithoutReturnBinding(string kind, bool globalT)
    {
        string libraryText = "namespace Names{public struct Box<T>{public T value;}public " + kind +
            " Helpers{public Box<T> Make<T>(Box<T> value)=>value;}}";
        string text = "using Names;" + (globalT ? "class T{public int Bogus;}" : "") +
            "readonly(char)* Name(){Helpers*? helper;return nameof(helper.Make);}";
        var library = new SourceFile(Path.GetFullPath("nameof-generic-library.gf"), libraryText);
        var source = new SourceFile(Path.GetFullPath("nameof-generic-client.gf"), text);
        SourceFile[] files = [library, source];
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var spans = snapshot.Nodes.Select(entry => (entry.Node, entry.Node.Span)).ToArray();
        var classes = snapshot.Checker!.Symbols.Classes.Keys.Order().ToArray();
        var structs = snapshot.Checker.Symbols.Structs.Keys.Order().ToArray();
        Assert.DoesNotContain(snapshot.Checker.Symbols.Structs.Values, info => info.Declaration?.SourceName == "Box");

        var model = new EditorModel(snapshot, files);
        int member = text.LastIndexOf("Make", StringComparison.Ordinal);
        int query = text.IndexOf("nameof", StringComparison.Ordinal);
        int declaration = libraryText.IndexOf("Make<T>", StringComparison.Ordinal);
        var expected = model.Definition(library.Path, declaration);
        Assert.NotNull(expected);
        for (int iteration = 0; iteration < 2; iteration++)
        {
            string hover = Json(model.Hover(source.Path, member)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("Box<T>", hover);
            Assert.Contains("Make<T>", hover);
            Assert.DoesNotContain("Bogus", hover);
            Assert.DoesNotContain('$', hover);
            Assert.Equal(Json(expected).GetRawText(), Json(model.Definition(source.Path, member)).GetRawText());
            string preview = Json(model.Hover(source.Path, query)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("\"Make\"", preview);
            Assert.Contains("char", preview);
        }
        Assert.Equal(classes, snapshot.Checker.Symbols.Classes.Keys.Order());
        Assert.Equal(structs, snapshot.Checker.Symbols.Structs.Keys.Order());
        foreach (var (node, span) in spans) Assert.Equal(span, node.Span);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
