using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ExtensionBlockEditorTests
{
    [Fact]
    public void GenericBlockBindingsKeepSourceParametersAndNavigation()
    {
        const string text = "struct Box<T>{public T Value;} extension<T> Box<T>{public T Echo(T item)=>item; public U Other<U>(U item)=>item;} int Main(){Box<int> box=new();return box.Echo(42)+box.Other<int>(1);}";
        var (model, source) = Analyze(text);
        int use = text.LastIndexOf("Echo(", StringComparison.Ordinal);
        string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int Echo(int item)", hover);
        Assert.DoesNotContain('$', hover);
        var expected = new Location(Workspace.FileUri(source.Path), Workspace.ToRange(source.Span(text.IndexOf("Echo(", StringComparison.Ordinal), 4)));
        Assert.Equal(expected, Assert.IsType<Location>(model.Definition(source.Path, use)));
        var signature = Json(model.SignatureHelp(source.Path, use + "Echo(".Length)).GetProperty("signatures")[0];
        Assert.Single(signature.GetProperty("parameters").EnumerateArray());
        Assert.Contains("int item", signature.GetProperty("label").GetString());
        int other = text.LastIndexOf("Other<int>", StringComparison.Ordinal);
        Assert.Contains("Other<int>(int item)", Json(model.Hover(source.Path, other)).GetProperty("contents").GetProperty("value").GetString());
        var completion = Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(completion, item => item.GetProperty("label").GetString() == "Echo");
        Assert.DoesNotContain(completion, item => item.GetProperty("label").GetString()!.Contains('$'));
    }

    [Fact]
    public void BlockTypeParameterHasItsOwnSourceScopeAndClassification()
    {
        const string text = "struct Box<T>{public T Value;} extension<U> Box<U>{public U Echo(U item)=>item;} int Main()=>0;";
        var (model, source) = Analyze(text);
        int declaration = text.IndexOf("<U>", StringComparison.Ordinal) + 1;
        int use = text.IndexOf("U Echo", StringComparison.Ordinal);
        Assert.Equal(new Location(Workspace.FileUri(source.Path), Workspace.ToRange(source.Span(declaration, 1))),
            Assert.IsType<Location>(model.Definition(source.Path, use)));
        var data = Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int column = 0; string? classification = null;
        for (int i = 0; i < data.Length; i += 5) {
            Assert.Equal(0, data[i]); column += data[i + 1];
            if (column == use) classification = EditorModel.TokenTypes[data[i + 3]];
        }
        Assert.Equal("typeParameter", classification);
        int outside = text.IndexOf("Main", StringComparison.Ordinal);
        Assert.DoesNotContain(Json(model.Completion(source.Path, outside)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "U");
        Assert.DoesNotContain('$', Json(model.DocumentSymbols(source.Path)).ToString());
    }

    private static (EditorModel Model, SourceFile Source) Analyze(string text)
    {
        var source = new SourceFile(Path.GetFullPath("extension-block-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true, NoBuiltins = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (new EditorModel(snapshot, [source]), source);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
