using System.Text.Json;
using gflat.LanguageServer;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public class StaticStorageEditorTests
{
    [Theory]
    [InlineData("using State; int F() { | return 0; }")]
    [InlineData("int F() { return State::|; }")]
    public void PrivateGlobalsAreNotOfferedAcrossFiles(string application)
    {
        var (model, path, offset, _) = Analyze(application,
            "namespace State; private int Hidden = 7; public int Count = 42;");
        var labels = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Count", labels);
        Assert.DoesNotContain("Hidden", labels);
    }

    [Fact]
    public void PrivateGlobalsRemainVisibleInTheirOwnFile()
    {
        var (model, path, offset, _) = Analyze("namespace State; private int Hidden = 7; int F() { | return 0; }");
        Assert.Contains(Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "Hidden");
    }

    [Theory]
    [InlineData("namespace State; public int Count = 42;", "int F() => State::Co|unt;")]
    [InlineData("class State { public static int Count = 42; }", "int F() => State::Co|unt;")]
    public void GlobalAndStaticStorageHaveHoverAndNavigation(string declarations, string application)
    {
        var (model, path, offset, _) = Analyze(application, declarations);
        string hover = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("int State::Count", hover);
        Assert.EndsWith("storage-library.gf", Json(model.Definition(path, offset)).GetProperty("uri").GetString());
    }

    [Fact]
    public void StaticConstructorHasSourceNameHoverNavigationAndOutline()
    {
        var (model, path, offset, snapshot) = Analyze("class State { public static int Count; static Sta|te() { Count = 42; } }");
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        string hover = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("static State::State()", hover);
        Assert.DoesNotContain("$cctor", hover);
        Assert.DoesNotContain("void", hover);
        Assert.DoesNotContain("private", hover);
        Assert.NotNull(model.Definition(path, offset));
        var children = Json(model.DocumentSymbols(path))[0].GetProperty("children").EnumerateArray().ToArray();
        Assert.Contains(children, child => child.GetProperty("name").GetString() == "State" && child.GetProperty("kind").GetInt32() == 9);
    }

    [Fact]
    public void StaticConstructorIsNotAnOrdinaryCallableMember()
    {
        var (model, path, offset, _) = Analyze("class State { public static int Count; static State() { Count = 42; } } int F() { return State::|; }");
        var labels = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Count", labels);
        Assert.DoesNotContain("State", labels);
        Assert.DoesNotContain("$cctor", labels);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static (EditorModel Model, string Path, int Offset, AnalysisSnapshot Snapshot) Analyze(string text, string? library = null)
    {
        int offset = text.IndexOf('|');
        string path = Path.GetFullPath("storage-use.gf");
        var files = new List<SourceFile> { new(path, text.Replace("|", "")) };
        if (library != null) files.Add(new(Path.GetFullPath("storage-library.gf"), library));
        var snapshot = new AnalysisSnapshot(files);
        // Completion snippets can be incomplete at the caret. Symbol visibility
        // still needs to follow the same accessibility rules as valid programs.
        return (new EditorModel(snapshot, files), path, offset, snapshot);
    }
}
