using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CodeActionMacroOptionsTests
{
    [Fact]
    public void GeneratedConstructorPreservesIdentifiersUnderProjectDefines()
    {
        const string code = "struct Item { private int @this; }";
        var source = new SourceFile(Path.GetFullPath("code-action-project-defines.gf"), code);
        var options = new CompilationOptions { NoStdLib = true, Defines = new Dictionary<string, string> {
            ["this"] = "99", ["thisValue"] = "99"
        } };
        var model = new EditorModel(new AnalysisSnapshot([source], options), [source]);
        var action = Assert.Single(model.CodeActions(source.Path, Workspace.ToRange(source.Span(code.IndexOf("Item", StringComparison.Ordinal), 0))),
            action => action.Title == "Generate constructor");
        var edit = Assert.Single(action.Edit!.Changes[Workspace.FileUri(source.Path)]);
        int start = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
        int end = Workspace.Offset(code, JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
        var changed = new SourceFile(source.Path, code.Remove(start, end - start).Insert(start, edit.NewText));
        var checkedChange = new AnalysisSnapshot([changed], options);
        Assert.DoesNotContain(checkedChange.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
