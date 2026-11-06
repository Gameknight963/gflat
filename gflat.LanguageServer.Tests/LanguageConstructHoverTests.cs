using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class LanguageConstructHoverTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("return")]
    [InlineData("while")]
    public void UnresolvedEscapedNamesAreNotExplainedAsLanguageKeywords(string name)
    {
        string code = "int Read() => @" + name + ";";
        var source = new SourceFile(Path.GetFullPath("escaped-learning-hover.gf"), code);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true, NoBuiltins = true }), [source]);
        Assert.Null(model.Hover(source.Path, code.IndexOf('@') + 1, options: new(ShowLanguageConstructs: true)));
    }

    [Theory]
    [InlineData("struct Sample { public int Value; }", "struct", "value type")]
    [InlineData("#define NUMBER 42\nint main() => NUMBER;", "define", "macro")]
    [InlineData("void F() { if (true) return; }", "if", "condition is true")]
    [InlineData("#if 0\nignored\n#endif\nint main() => 0;", "if", "preprocessor condition")]
    [InlineData("void F() { static if (true) {} }", "static", "compile time")]
    public void ConstructExplanationsAreOptIn(string code, string word, string explanation)
    {
        var source = new SourceFile(Path.GetFullPath("learning-hovers.gf"), code);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true, NoBuiltins = true });
        var model = new EditorModel(snapshot, [source]);
        int offset = code.IndexOf(word, StringComparison.Ordinal) + 1;
        Assert.Null(model.Hover(source.Path, offset));
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset, options: new(ShowLanguageConstructs: true)), Protocol.JsonOptions);
        Assert.Contains(explanation, hover.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void SettingsPreserveDefaultsUntilExplicitlyEnabled()
    {
        Assert.False(new HoverOptions().ShowLanguageConstructs);
        var settings = JsonSerializer.SerializeToElement(new { gflat = new { showLanguageConstructs = true } });
        Assert.True(new HoverOptions().Update(settings).ShowLanguageConstructs);
    }
}
