using System.Text.Json;
using gflat.Preprocessing;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class MacroToolsTests
{
    [Theory]
    [InlineData("#define VALUE 4\n#define DOUBLE(x) ((x) + (x))\nint F() => DOUBLE(VALUE);", "DOUBLE(VALUE)")]
    [InlineData("#define SELF SELF\nint SELF = 7; int F() => SELF;", "SELF;")]
    [InlineData("#define EMPTY\nint F() { EMPTY return 0; }", "EMPTY return")]
    [InlineData("int F() => __LINE__;", "__LINE__")]
    [InlineData("#define NAME @class\nint @class = 7; int F() => NAME;", "NAME;")]
    public void InlineExpansionPreservesThePreprocessedTokenSequence(string code, string marker)
    {
        var source = new SourceFile(Path.GetFullPath("macro-tools.gf"), code);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        var model = new EditorModel(snapshot, [source]);
        int offset = code.LastIndexOf(marker, StringComparison.Ordinal);
        var range = Workspace.ToRange(source.Span(offset, 0));
        var action = Assert.Single(model.CodeActions(source.Path, range), a => a.Title == "Expand macro inline");
        var edit = Assert.Single(action.Edit!.Changes[Workspace.FileUri(source.Path)]);
        int start = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
        int end = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
        var changed = new SourceFile(source.Path, code.Remove(start, end - start).Insert(start, edit.NewText));
        string[] Expand(SourceFile file) => new Preprocessor(new() { NoStdLib = true }, new DiagnosticBag()).Process(file).Tokens.Select(t => t.Kind + ":" + t.Text).ToArray();
        Assert.Equal(Expand(source), Expand(changed));
    }

    [Fact]
    public void InspectionIncludesSubstitutionAndRescanningSteps()
    {
        const string code = "#define A 4\n#define B(x) (x + A)\n#define C B(A)\nint F() => C;";
        var source = new SourceFile(Path.GetFullPath("macro-inspection.gf"), code);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        string text = model.MacroInspection(source.Path, code.LastIndexOf('C'))!;
        Assert.Contains("1. C", text);
        Assert.Contains("B", text);
        Assert.Contains("A", text);
        Assert.Contains("Expanded tokens:", text);
        Assert.Contains("4 + 4", text);
    }
}
